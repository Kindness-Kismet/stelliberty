use std::path::Path;
use std::time::Duration;

use anyhow::{Context, Result, anyhow};
use futures_util::StreamExt;
#[cfg(unix)]
use interprocess::local_socket::{GenericFilePath, ToFsName};
#[cfg(windows)]
use interprocess::local_socket::{GenericNamespaced, ToNsName};
use interprocess::local_socket::{
    tokio::Stream as TokioIpcStream, traits::tokio::Stream as TokioStreamExt,
};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio_tungstenite::{client_async, tungstenite::Message};

// mihomo 管道 HTTP 客户端每次新建连接，避免 keep-alive 状态过期。
#[derive(Debug, Clone)]
pub struct CoreApiClient {
    namespace: String,
}

#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("mihomo API was not ready after {0} polls")]
    Timeout(u32),
    #[error("mihomo API returned error status {0}: {1}")]
    Status(u16, String),
    #[error("mihomo rejected the candidate config: {0}")]
    ConfigRejected(String),
}

impl CoreApiClient {
    pub fn new(pipe_path: &str) -> Self {
        // Windows 接受完整管道路径；Unix 要求绝对 socket 文件路径。
        #[cfg(windows)]
        let namespace = pipe_path
            .strip_prefix(r"\\.\pipe\")
            .unwrap_or(pipe_path)
            .to_owned();
        #[cfg(unix)]
        let namespace = pipe_path.to_owned();
        Self { namespace }
    }

    pub async fn wait_ready(&self, max_retries: u32, interval: Duration) -> Result<String> {
        for attempt in 0..max_retries {
            if let Ok((status, body)) = self.request("GET", "/version", None).await
                && (200..300).contains(&status)
            {
                return Ok(body);
            }
            if attempt + 1 < max_retries {
                tokio::time::sleep(interval).await;
            }
        }
        Err(ApiError::Timeout(max_retries).into())
    }

    pub async fn put_config_path(&self, yaml_path: &Path) -> Result<()> {
        let path_str = yaml_path.to_string_lossy().into_owned();
        let body = serde_json::json!({ "path": path_str }).to_string();
        let (status, body) = self
            .request("PUT", "/configs?force=true", Some(body))
            .await?;
        if (200..300).contains(&status) {
            return Ok(());
        }
        // 配置接口的 4xx 都表示候选配置被拒绝，不能继续走重启回退。
        if (400..500).contains(&status) {
            return Err(ApiError::ConfigRejected(body).into());
        }
        Err(ApiError::Status(status, body).into())
    }

    pub async fn close_all_connections(&self) -> Result<()> {
        let (status, body) = self.request("DELETE", "/connections", None).await?;
        if (200..300).contains(&status) {
            Ok(())
        } else {
            Err(ApiError::Status(status, body).into())
        }
    }

    pub async fn stream_logs<F>(&self, level: &str, mut on_line: F) -> Result<()>
    where
        F: FnMut(String) + Send + 'static,
    {
        let stream = self.connect_stream().await?;
        let uri = format!("ws://localhost/logs?level={level}");
        let (mut ws_stream, _) =
            tokio::time::timeout(Duration::from_secs(5), client_async(uri, stream))
                .await
                .context("Timed out connecting to mihomo log WebSocket")?
                .context("Failed to connect to mihomo log WebSocket")?;

        while let Some(message) = ws_stream.next().await {
            match message.context("Failed to read mihomo log WebSocket")? {
                Message::Text(text) => on_line(text.to_string()),
                Message::Binary(data) => on_line(String::from_utf8_lossy(&data).into_owned()),
                Message::Close(_) => break,
                Message::Ping(_) | Message::Pong(_) | Message::Frame(_) => {}
            }
        }

        Ok(())
    }

    async fn request(
        &self,
        method: &str,
        path: &str,
        body: Option<String>,
    ) -> Result<(u16, String)> {
        let mut stream = self.connect_stream().await?;

        // 请求强制 Connection: close；无长度响应读取到 EOF。
        let mut request =
            format!("{method} {path} HTTP/1.1\r\nHost: mihomo\r\nConnection: close\r\n");
        match &body {
            Some(b) => {
                request.push_str(&format!(
                    "Content-Type: application/json\r\nContent-Length: {}\r\n",
                    b.len()
                ));
            }
            None => {
                request.push_str("Content-Length: 0\r\n");
            }
        }
        request.push_str("\r\n");
        if let Some(b) = &body {
            request.push_str(b);
        }

        let request_total = tokio::time::timeout(Duration::from_secs(5), async {
            stream.write_all(request.as_bytes()).await?;
            stream.flush().await
        })
        .await
        .context("Timed out writing mihomo HTTP request")?;
        request_total.context("Failed to write mihomo HTTP request")?;

        let mut reader = BufReader::new(stream);

        let mut status_line = String::new();
        tokio::time::timeout(Duration::from_secs(5), reader.read_line(&mut status_line))
            .await
            .context("Timed out reading HTTP status line")?
            .context("Failed to read HTTP status line")?;
        let status = parse_status_code(&status_line)?;

        let mut content_length: Option<usize> = None;
        let mut chunked = false;
        loop {
            let mut header = String::new();
            tokio::time::timeout(Duration::from_secs(5), reader.read_line(&mut header))
                .await
                .context("Timed out reading HTTP header")?
                .context("Failed to read HTTP header")?;
            if header == "\r\n" || header == "\n" || header.is_empty() {
                break;
            }
            let lower = header.to_ascii_lowercase();
            if let Some(rest) = lower.strip_prefix("content-length:") {
                content_length = rest.trim().parse().ok();
            } else if let Some(rest) = lower.strip_prefix("transfer-encoding:")
                && rest.trim().contains("chunked")
            {
                chunked = true;
            }
        }

        let body = if chunked {
            read_chunked(&mut reader).await?
        } else if let Some(len) = content_length {
            let mut buf = vec![0u8; len];
            tokio::time::timeout(Duration::from_secs(10), reader.read_exact(&mut buf))
                .await
                .context("Timed out reading body")?
                .context("Failed to read body")?;
            String::from_utf8_lossy(&buf).into_owned()
        } else {
            let mut buf = String::new();
            tokio::time::timeout(Duration::from_secs(10), reader.read_to_string(&mut buf))
                .await
                .context("Timed out reading body until EOF")?
                .context("Failed to read body until EOF")?;
            buf
        };

        Ok((status, body))
    }

    async fn connect_stream(&self) -> Result<TokioIpcStream> {
        #[cfg(unix)]
        let name = {
            if !self.namespace.starts_with('/') {
                return Err(anyhow!("mihomo unix socket must use an absolute path"));
            }
            self.namespace
                .as_str()
                .to_fs_name::<GenericFilePath>()
                .context("Failed to convert mihomo unix socket path")?
        };
        #[cfg(windows)]
        let name = self
            .namespace
            .as_str()
            .to_ns_name::<GenericNamespaced>()
            .context("Failed to convert mihomo pipe name")?;
        tokio::time::timeout(Duration::from_secs(5), TokioIpcStream::connect(name))
            .await
            .context("Timed out connecting to mihomo pipe")?
            .context("Failed to connect to mihomo pipe")
    }
}

fn parse_status_code(line: &str) -> Result<u16> {
    let mut parts = line.split_whitespace();
    parts
        .next()
        .ok_or_else(|| anyhow!("HTTP status line is empty"))?;
    let code = parts
        .next()
        .ok_or_else(|| anyhow!("HTTP status line is missing a status code"))?;
    code.parse::<u16>()
        .context("Failed to parse HTTP status code")
}

async fn read_chunked<R>(reader: &mut R) -> Result<String>
where
    R: tokio::io::AsyncBufRead + Unpin,
{
    let mut result = Vec::new();
    loop {
        let mut size_line = String::new();
        reader
            .read_line(&mut size_line)
            .await
            .context("Failed to read chunk size")?;
        let size_str = size_line.trim();
        if size_str.is_empty() {
            continue;
        }
        let size = usize::from_str_radix(size_str, 16).context("Failed to parse chunk size")?;
        if size == 0 {
            // 零长度 chunk 后跟尾部空行，需要额外消费。
            let mut trailer = String::new();
            reader.read_line(&mut trailer).await.ok();
            break;
        }
        let mut buf = vec![0u8; size];
        AsyncReadExt::read_exact(reader, &mut buf)
            .await
            .context("Failed to read chunk content")?;
        result.extend_from_slice(&buf);
        let mut crlf = String::new();
        reader.read_line(&mut crlf).await.ok();
    }
    Ok(String::from_utf8_lossy(&result).into_owned())
}
