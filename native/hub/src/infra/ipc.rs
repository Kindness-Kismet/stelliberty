use std::sync::Arc;

#[cfg(unix)]
use anyhow::bail;
use anyhow::{Context, Result};
#[cfg(unix)]
use interprocess::local_socket::{GenericFilePath, ToFsName};
#[cfg(windows)]
use interprocess::local_socket::{GenericNamespaced, ToNsName};
use interprocess::local_socket::{
    ListenerOptions, tokio::Stream as TokioIpcStream, traits::tokio::Listener as TokioListenerExt,
};
use serde::{Deserialize, Serialize};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::sync::{Mutex, broadcast, mpsc};

// 单帧上限 4 MiB；配置内容随 payload 传输，预留 JSON 转义膨胀余量。
pub const MAX_LINE_BYTES: usize = 4 * 1024 * 1024;

#[derive(Debug, Serialize, Deserialize)]
#[serde(untagged)]
pub enum Incoming {
    Request {
        id: String,
        method: String,
        #[serde(default)]
        params: serde_json::Value,
    },
}

#[derive(Debug, Serialize, Clone)]
#[serde(untagged)]
pub enum Outgoing {
    Response {
        id: String,
        result: serde_json::Value,
    },
    Error {
        id: String,
        error: ErrorBody,
    },
    Event {
        event: String,
        data: serde_json::Value,
    },
}

#[derive(Debug, Serialize, Clone)]
pub struct ErrorBody {
    pub code: String,
    pub message: String,
}

#[async_trait::async_trait]
pub trait MethodHandler: Send + Sync {
    async fn handle(
        &self,
        method: &str,
        params: serde_json::Value,
    ) -> std::result::Result<serde_json::Value, ErrorBody>;
}

pub struct IpcServer {
    handler: Arc<dyn MethodHandler>,
    event_tx: broadcast::Sender<Outgoing>,
    shutdown_rx: Mutex<Option<mpsc::Receiver<()>>>,
}

impl IpcServer {
    pub fn new(
        handler: Arc<dyn MethodHandler>,
    ) -> (Arc<Self>, broadcast::Sender<Outgoing>, mpsc::Sender<()>) {
        let (event_tx, _) = broadcast::channel::<Outgoing>(128);
        Self::with_events(handler, event_tx)
    }

    pub fn with_events(
        handler: Arc<dyn MethodHandler>,
        event_tx: broadcast::Sender<Outgoing>,
    ) -> (Arc<Self>, broadcast::Sender<Outgoing>, mpsc::Sender<()>) {
        let (shutdown_tx, shutdown_rx) = mpsc::channel(1);
        let server = Arc::new(Self {
            handler,
            event_tx: event_tx.clone(),
            shutdown_rx: Mutex::new(Some(shutdown_rx)),
        });
        (server, event_tx, shutdown_tx)
    }

    pub async fn serve(self: Arc<Self>, pipe_name: &str) -> Result<()> {
        #[cfg(unix)]
        let name = {
            if !pipe_name.starts_with('/') {
                bail!("Unix IPC socket must use an absolute path");
            }
            remove_stale_socket(pipe_name)?;
            pipe_name
                .to_fs_name::<GenericFilePath>()
                .context("Failed to convert Unix IPC socket name")?
        };
        #[cfg(windows)]
        let name = pipe_name
            .to_ns_name::<GenericNamespaced>()
            .context("Failed to convert pipe name")?;
        let listener = ListenerOptions::new()
            .name(name)
            .create_tokio()
            .context("Failed to create named-pipe listener")?;
        #[cfg(unix)]
        apply_socket_permissions(pipe_name)?;
        let mut shutdown_rx = self
            .shutdown_rx
            .lock()
            .await
            .take()
            .context("server has already been started")?;
        loop {
            tokio::select! {
                _ = shutdown_rx.recv() => break,
                accept = listener.accept() => {
                    match accept {
                        Ok(stream) => {
                            let handler = self.handler.clone();
                            let event_tx = self.event_tx.clone();
                            tokio::spawn(async move {
                                if let Err(e) = handle_connection(stream, handler, event_tx).await {
                                    tracing::warn!("ipc connection handling failed: {e}");
                                }
                            });
                        }
                        Err(e) => tracing::warn!("ipc accept failed: {e}"),
                    }
                }
            }
        }
        Ok(())
    }
}

#[cfg(unix)]
fn remove_stale_socket(path: &str) -> Result<()> {
    use std::os::unix::fs::FileTypeExt;

    let path = std::path::Path::new(path);
    if !path.exists() {
        return Ok(());
    }
    let metadata = std::fs::metadata(path)
        .with_context(|| format!("Failed to read stale IPC socket: {}", path.display()))?;
    if !metadata.file_type().is_socket() {
        bail!(
            "IPC socket path is occupied by a non-socket file: {}",
            path.display()
        );
    }
    std::fs::remove_file(path)
        .with_context(|| format!("Failed to remove stale IPC socket: {}", path.display()))
}

#[cfg(unix)]
fn apply_socket_permissions(path: &str) -> Result<()> {
    use std::os::unix::fs::PermissionsExt;

    // Hub 和本地客户端共享所有者；IPC 不对其他本地用户暴露。
    let permissions = std::fs::Permissions::from_mode(0o600);
    std::fs::set_permissions(path, permissions)
        .with_context(|| format!("Failed to set IPC socket permissions: {path}"))
}

async fn handle_connection(
    stream: TokioIpcStream,
    handler: Arc<dyn MethodHandler>,
    event_tx: broadcast::Sender<Outgoing>,
) -> Result<()> {
    let stream = Arc::new(stream);
    let mut reader = BufReader::new(stream.as_ref());
    // 读取通过共享引用进行；写锁保证事件与响应不会交错。
    let writer = Arc::new(Mutex::new(stream.clone()));
    let mut event_rx = event_tx.subscribe();

    let writer_for_events = writer.clone();
    // 事件随连接持续流式发送，请求仍由独立任务处理。
    let event_task = tokio::spawn(async move {
        while let Ok(ev) = event_rx.recv().await {
            let Ok(line) = serde_json::to_string(&ev) else {
                continue;
            };
            let stream = writer_for_events.lock().await;
            let mut w = stream.as_ref();
            if w.write_all(line.as_bytes()).await.is_err() {
                break;
            }
            if w.write_all(b"\n").await.is_err() {
                break;
            }
        }
    });

    let mut line = String::new();
    let read_result: Result<()> = async {
        loop {
            line.clear();
            // take 限制单帧大小，避免无换行流绕过 read_line。
            let read = (&mut reader)
                .take(MAX_LINE_BYTES as u64 + 1)
                .read_line(&mut line)
                .await
                .context("ipc read failed")?;
            if read == 0 {
                break;
            }
            if line.len() > MAX_LINE_BYTES {
                tracing::warn!("ipc frame is too large: {} bytes", line.len());
                break;
            }
            let trimmed = line.trim_end();
            if trimmed.is_empty() {
                continue;
            }
            let req: Incoming = match serde_json::from_str(trimmed) {
                Ok(v) => v,
                Err(e) => {
                    tracing::warn!("ipc request parse failed: {e}");
                    continue;
                }
            };
            let Incoming::Request { id, method, params } = req;
            let handler_cl = handler.clone();
            let writer_cl = writer.clone();
            tokio::spawn(async move {
                let outgoing = match handler_cl.handle(&method, params).await {
                    Ok(result) => Outgoing::Response { id, result },
                    Err(error) => Outgoing::Error { id, error },
                };
                if let Ok(s) = serde_json::to_string(&outgoing) {
                    let stream = writer_cl.lock().await;
                    let mut w = stream.as_ref();
                    let _ = w.write_all(s.as_bytes()).await;
                    let _ = w.write_all(b"\n").await;
                }
            });
        }
        Ok(())
    }
    .await;

    event_task.abort();
    read_result
}
