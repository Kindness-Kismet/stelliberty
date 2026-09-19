use std::sync::Arc;
use std::time::{Duration, Instant};

use anyhow::{Context, Result, anyhow, bail};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::sync::{Mutex, oneshot};

use crate::channel::{command_endpoint, service_name};
use crate::core::{CoreManager, CoreState, StartCoreRequest};
use crate::heartbeat::HeartbeatWatchdog;
use crate::logging;
use crate::protocol::{ServiceCommand, ServiceResponse};
use crate::service_version;
use hub::infra::core_api::ApiError;

const MAX_REQUEST_BYTES: usize = 1024 * 1024;
const MAX_RESPONSE_BYTES: usize = 1024 * 1024;

#[derive(Clone)]
pub struct ServiceState {
    started_at: Instant,
    last_heartbeat: Arc<Mutex<Option<Instant>>>,
    core: CoreManager,
    shutdown: Arc<Mutex<Option<oneshot::Sender<()>>>>,
}

impl ServiceState {
    pub fn new(shutdown: oneshot::Sender<()>) -> Self {
        Self {
            started_at: Instant::now(),
            last_heartbeat: Arc::new(Mutex::new(None)),
            core: CoreManager::new(),
            shutdown: Arc::new(Mutex::new(Some(shutdown))),
        }
    }

    async fn handle(&self, command: ServiceCommand) -> ServiceResponse {
        match command {
            ServiceCommand::Status => self.status().await,
            ServiceCommand::Heartbeat => {
                *self.last_heartbeat.lock().await = Some(Instant::now());
                self.restore_core_if_needed().await;
                ServiceResponse::HeartbeatAck
            }
            ServiceCommand::Logs { lines } => ServiceResponse::Logs {
                lines: logging::recent(lines),
            },
            ServiceCommand::StartCore {
                mihomo_path,
                config_path,
                data_core_dir,
            } => {
                let request = StartCoreRequest {
                    mihomo_path,
                    config_path,
                    data_core_dir,
                };
                match self.core.start(request).await {
                    Ok(pid) => ServiceResponse::Success {
                        message: Some(format!("core started pid={pid}")),
                    },
                    Err(error) => ServiceResponse::Error {
                        code: "core.start_failed".to_string(),
                        message: error.to_string(),
                    },
                }
            }
            ServiceCommand::ApplyCoreConfig { config_path } => {
                match self.core.apply_config(config_path).await {
                    Ok(outcome) => ServiceResponse::CoreConfigApplied {
                        mode: outcome.mode.to_string(),
                        pid: outcome.pid,
                    },
                    Err(error) => {
                        // 4xx 拒绝不可回退重启；错误码随回执文本透传，便于日志定位
                        let rejected = error
                            .downcast_ref::<ApiError>()
                            .is_some_and(|api| matches!(api, ApiError::ConfigRejected(_)));
                        ServiceResponse::Error {
                            code: if rejected {
                                "core.config_rejected".to_string()
                            } else {
                                "core.apply_failed".to_string()
                            },
                            message: format!("{error:#}"),
                        }
                    }
                }
            }
            ServiceCommand::StopCore => match self.core.stop().await {
                Ok(()) => ServiceResponse::Success {
                    message: Some("core stopped".to_string()),
                },
                Err(error) => ServiceResponse::Error {
                    code: "core.stop_failed".to_string(),
                    message: error.to_string(),
                },
            },
            ServiceCommand::RestartCore => match self.core.restart().await {
                Ok(pid) => ServiceResponse::Success {
                    message: Some(format!("core restarted pid={pid}")),
                },
                Err(error) => ServiceResponse::Error {
                    code: "core.restart_failed".to_string(),
                    message: error.to_string(),
                },
            },
            ServiceCommand::Shutdown => ServiceResponse::Success {
                message: Some("Service is stopping".to_string()),
            },
        }
    }

    async fn shutdown(&self) {
        logging::info("Service shutdown request received");
        self.stop_core().await;
        let shutdown = self.shutdown.lock().await.take();
        if let Some(sender) = shutdown {
            let _ = sender.send(());
        }
    }

    pub async fn stop_core(&self) {
        let status = self.core.status().await;
        let started_at = Instant::now();
        logging::info(format!(
            "Service core shutdown started: state={} pid={}",
            core_state_text(status.state),
            status
                .pid
                .map_or_else(|| "none".to_string(), |pid| pid.to_string())
        ));
        match self.core.stop_for_service_shutdown().await {
            Ok(()) => logging::info(format!(
                "Service core shutdown completed: elapsed={}ms",
                started_at.elapsed().as_millis()
            )),
            Err(error) => logging::warn(format!(
                "Service core shutdown failed: elapsed={}ms error={error:#}",
                started_at.elapsed().as_millis()
            )),
        }
    }
    async fn status(&self) -> ServiceResponse {
        let last_heartbeat_seconds = self
            .last_heartbeat
            .lock()
            .await
            .map(|instant| instant.elapsed().as_secs());

        let core = self.core.status().await;
        ServiceResponse::Status {
            service_name: service_name().to_string(),
            version: service_version().to_string(),
            uptime_seconds: self.started_at.elapsed().as_secs(),
            last_heartbeat_seconds,
            core_state: core_state_text(core.state).to_string(),
            core_pid: core.pid,
            core_last_error: core.last_error,
        }
    }

    pub async fn monitor_heartbeat(&self) {
        let mut watchdog = HeartbeatWatchdog::new(Instant::now());
        loop {
            tokio::time::sleep(crate::heartbeat::CHECK_INTERVAL).await;
            let now = Instant::now();
            if let Some(gap) = watchdog.observe(now) {
                logging::info(format!(
                    "Heartbeat watchdog resumed after scheduler gap: gap_ms={} grace_s={}",
                    gap.as_millis(),
                    crate::heartbeat::TIMEOUT.as_secs()
                ));
            }
            self.core.cleanup_orphan_cores().await;
            // 超时停止与续接互斥，避免新心跳先恢复核心、旧判定随后又将其停止。
            let mut heartbeat = self.last_heartbeat.lock().await;
            if !watchdog.should_expire(Instant::now(), *heartbeat) {
                continue;
            }

            self.core.stop_for_heartbeat_timeout().await;
            *heartbeat = None;
        }
    }

    async fn restore_core_if_needed(&self) {
        match self.core.restore_if_needed().await {
            Ok(true) => logging::info("Client heartbeat recovered; core restarted"),
            Ok(false) => {}
            Err(error) => logging::warn(format!(
                "Failed to restore core after heartbeat recovery: {error:#}"
            )),
        }
    }
}

fn core_state_text(state: CoreState) -> &'static str {
    match state {
        CoreState::Running => "running",
        CoreState::Stopping => "stopping",
        CoreState::Stopped => "stopped",
        CoreState::Crashed => "crashed",
    }
}

pub async fn run_server(state: ServiceState, mut shutdown_rx: oneshot::Receiver<()>) -> Result<()> {
    logging::info(format!("Service IPC listening on {}", command_endpoint()));
    #[cfg(windows)]
    {
        run_windows(state, &mut shutdown_rx).await
    }

    #[cfg(not(windows))]
    {
        run_unix(state, &mut shutdown_rx).await
    }
}

pub async fn send_command(command: ServiceCommand, timeout: Duration) -> Result<ServiceResponse> {
    let task = async {
        #[cfg(windows)]
        let mut stream = connect_windows()?;

        #[cfg(not(windows))]
        let mut stream = connect_unix().await?;

        write_frame(&mut stream, &command).await?;
        read_response(&mut stream).await
    };

    tokio::time::timeout(timeout, task)
        .await
        .context("Service IPC request timed out")?
}

#[cfg(windows)]
async fn run_windows(state: ServiceState, shutdown_rx: &mut oneshot::Receiver<()>) -> Result<()> {
    let security_descriptor = create_command_security_attributes()?;
    let mut is_first_instance = true;

    loop {
        let server = create_named_pipe_with_security(
            command_endpoint(),
            is_first_instance,
            &security_descriptor,
        )?;
        is_first_instance = false;

        tokio::select! {
            result = server.connect() => {
                if result.is_err() {
                    continue;
                }
                let state = state.clone();
                tokio::spawn(async move {
                    let _ = handle_client(server, state).await;
                });
            }
            _ = &mut *shutdown_rx => {
                break;
            }
        }
    }

    Ok(())
}

#[cfg(not(windows))]
async fn run_unix(state: ServiceState, shutdown_rx: &mut oneshot::Receiver<()>) -> Result<()> {
    use tokio::net::UnixListener;

    let endpoint = std::path::Path::new(command_endpoint());
    remove_stale_unix_socket(endpoint)?;

    let listener = UnixListener::bind(endpoint)
        .with_context(|| format!("Failed to create IPC socket: {}", endpoint.display()))?;
    let allowed_credential = unix_allowed_credential()?;
    apply_unix_socket_permissions(endpoint, allowed_credential)?;
    logging::info(format!(
        "Authorized Unix IPC user: uid={} gid={}",
        allowed_credential.uid, allowed_credential.gid
    ));

    loop {
        tokio::select! {
            result = listener.accept() => {
                let (stream, _) = result.context("Failed to accept IPC connection")?;
                let peer = match unix_peer_credential(&stream) {
                    Ok(peer) => peer,
                    Err(e) => {
                        logging::warn(format!("Rejected IPC connection with unknown peer identity: {e:#}"));
                        continue;
                    }
                };
                if !is_unix_peer_allowed(peer, allowed_credential) {
                    logging::warn(format!("Rejected unauthorized IPC connection: uid={} gid={}", peer.uid, peer.gid));
                    continue;
                }

                let state = state.clone();
                tokio::spawn(async move {
                    let _ = handle_client(stream, state).await;
                });
            }
            _ = &mut *shutdown_rx => {
                break;
            }
        }
    }

    let _ = std::fs::remove_file(endpoint);
    Ok(())
}

#[cfg(not(windows))]
#[derive(Clone, Copy)]
struct UnixCredential {
    uid: libc::uid_t,
    gid: libc::gid_t,
}

#[cfg(not(windows))]
fn remove_stale_unix_socket(endpoint: &std::path::Path) -> Result<()> {
    use std::os::unix::fs::FileTypeExt;

    let metadata = match std::fs::symlink_metadata(endpoint) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(error) => {
            return Err(error).with_context(|| {
                format!("Failed to read IPC path status: {}", endpoint.display())
            });
        }
    };

    if !metadata.file_type().is_socket() {
        bail!(
            "IPC path already exists and is not a socket: {}",
            endpoint.display()
        );
    }

    std::fs::remove_file(endpoint).with_context(|| {
        format!(
            "Failed to clean up stale IPC socket: {}",
            endpoint.display()
        )
    })
}

#[cfg(not(windows))]
fn unix_allowed_credential() -> Result<UnixCredential> {
    use std::os::unix::fs::MetadataExt;

    let binary_path =
        std::env::current_exe().context("Failed to get the service executable path")?;
    let metadata = std::fs::metadata(&binary_path).with_context(|| {
        format!(
            "Failed to read the service executable owner: {}",
            binary_path.display()
        )
    })?;
    Ok(UnixCredential {
        uid: metadata.uid(),
        gid: metadata.gid(),
    })
}

#[cfg(not(windows))]
fn apply_unix_socket_permissions(
    endpoint: &std::path::Path,
    credential: UnixCredential,
) -> Result<()> {
    use std::os::unix::ffi::OsStrExt;
    use std::os::unix::fs::PermissionsExt;

    // 安全前提：geteuid 无副作用，只读取 effective uid。
    let effective_uid = unsafe { libc::geteuid() };
    if effective_uid == 0 {
        let path = std::ffi::CString::new(endpoint.as_os_str().as_bytes())
            .context("IPC socket path contains a NUL byte")?;
        // 安全前提：path 是 NUL 结尾 C 字符串，uid/gid 来自文件元数据。
        let result = unsafe { libc::chown(path.as_ptr(), credential.uid, credential.gid) };
        if result != 0 {
            return Err(std::io::Error::last_os_error()).with_context(|| {
                format!("Failed to set IPC socket owner: {}", endpoint.display())
            });
        }
    } else if effective_uid != credential.uid {
        bail!(
            "Current user uid={} does not match service executable owner uid={}",
            effective_uid,
            credential.uid
        );
    }

    std::fs::set_permissions(endpoint, std::fs::Permissions::from_mode(0o600)).with_context(|| {
        format!(
            "Failed to set IPC socket permissions: {}",
            endpoint.display()
        )
    })
}

#[cfg(not(windows))]
fn is_unix_peer_allowed(peer: UnixCredential, allowed: UnixCredential) -> bool {
    peer.uid == 0 || peer.uid == allowed.uid
}

#[cfg(target_os = "linux")]
fn unix_peer_credential(stream: &tokio::net::UnixStream) -> Result<UnixCredential> {
    use std::os::fd::AsRawFd;

    // 安全前提：ucred 是 C POD 结构，getsockopt 在零初始化后填充。
    let mut credential: libc::ucred = unsafe { std::mem::zeroed() };
    let mut length = std::mem::size_of::<libc::ucred>() as libc::socklen_t;
    // 安全前提：fd 来自有效 UnixStream；缓冲区和长度指针保持有效。
    let result = unsafe {
        libc::getsockopt(
            stream.as_raw_fd(),
            libc::SOL_SOCKET,
            libc::SO_PEERCRED,
            std::ptr::addr_of_mut!(credential) as *mut libc::c_void,
            std::ptr::addr_of_mut!(length),
        )
    };
    if result != 0 {
        return Err(std::io::Error::last_os_error())
            .context("Failed to read Linux IPC peer credentials");
    }

    Ok(UnixCredential {
        uid: credential.uid,
        gid: credential.gid,
    })
}

#[cfg(target_os = "macos")]
fn unix_peer_credential(stream: &tokio::net::UnixStream) -> Result<UnixCredential> {
    use std::os::fd::AsRawFd;

    let mut uid: libc::uid_t = 0;
    let mut gid: libc::gid_t = 0;
    // 安全前提：fd 来自有效 UnixStream；uid/gid 指针保持有效。
    let result = unsafe { libc::getpeereid(stream.as_raw_fd(), &mut uid, &mut gid) };
    if result != 0 {
        return Err(std::io::Error::last_os_error())
            .context("Failed to read macOS IPC peer credentials");
    }

    Ok(UnixCredential { uid, gid })
}

#[cfg(all(not(windows), not(target_os = "linux"), not(target_os = "macos")))]
fn unix_peer_credential(_stream: &tokio::net::UnixStream) -> Result<UnixCredential> {
    bail!("IPC identity verification is not supported on this Unix platform yet")
}

async fn handle_client<S>(mut stream: S, state: ServiceState) -> Result<()>
where
    S: AsyncReadExt + AsyncWriteExt + Unpin,
{
    let command = read_command(&mut stream).await?;
    let should_shutdown = matches!(command, ServiceCommand::Shutdown);
    let response = state.handle(command).await;
    write_frame(&mut stream, &response).await?;
    if should_shutdown {
        state.shutdown().await;
    }
    Ok(())
}

async fn read_command<S>(stream: &mut S) -> Result<ServiceCommand>
where
    S: AsyncReadExt + Unpin,
{
    let payload = read_payload(stream, MAX_REQUEST_BYTES).await?;
    serde_json::from_slice(&payload).context("Failed to parse service command")
}

async fn read_response<S>(stream: &mut S) -> Result<ServiceResponse>
where
    S: AsyncReadExt + Unpin,
{
    let payload = read_payload(stream, MAX_RESPONSE_BYTES).await?;
    let response: ServiceResponse =
        serde_json::from_slice(&payload).context("Failed to parse service response")?;
    match response {
        ServiceResponse::Error { code, message } => Err(anyhow!("{code}: {message}")),
        other => Ok(other),
    }
}

async fn read_payload<S>(stream: &mut S, max_bytes: usize) -> Result<Vec<u8>>
where
    S: AsyncReadExt + Unpin,
{
    let mut len_buf = [0u8; 4];
    stream
        .read_exact(&mut len_buf)
        .await
        .context("Failed to read IPC frame length")?;
    let len = u32::from_le_bytes(len_buf) as usize;
    if len > max_bytes {
        bail!("IPC frame is too large: {len}");
    }

    let mut payload = vec![0u8; len];
    stream
        .read_exact(&mut payload)
        .await
        .context("Failed to read IPC frame data")?;
    Ok(payload)
}

async fn write_frame<S, T>(stream: &mut S, value: &T) -> Result<()>
where
    S: AsyncWriteExt + Unpin,
    T: serde::Serialize,
{
    let payload = serde_json::to_vec(value).context("Failed to serialize IPC data")?;
    if payload.len() > MAX_RESPONSE_BYTES {
        bail!("IPC response is too large: {}", payload.len());
    }
    let len = u32::try_from(payload.len()).context("IPC data length overflowed u32")?;
    stream
        .write_all(&len.to_le_bytes())
        .await
        .context("Failed to write IPC frame length")?;
    stream
        .write_all(&payload)
        .await
        .context("Failed to write IPC frame data")?;
    stream.flush().await.context("Failed to flush IPC data")
}

#[cfg(windows)]
fn connect_windows() -> Result<tokio::net::windows::named_pipe::NamedPipeClient> {
    tokio::net::windows::named_pipe::ClientOptions::new()
        .open(command_endpoint())
        .with_context(|| format!("Failed to connect to service pipe: {}", command_endpoint()))
}

#[cfg(not(windows))]
async fn connect_unix() -> Result<tokio::net::UnixStream> {
    tokio::net::UnixStream::connect(command_endpoint())
        .await
        .with_context(|| {
            format!(
                "Failed to connect to service socket: {}",
                command_endpoint()
            )
        })
}

#[cfg(windows)]
struct SecurityDescriptorWrapper(*mut std::ffi::c_void);

#[cfg(windows)]
unsafe impl Send for SecurityDescriptorWrapper {}

#[cfg(windows)]
impl Drop for SecurityDescriptorWrapper {
    fn drop(&mut self) {
        if self.0.is_null() {
            return;
        }

        // Windows 分配的安全描述符必须配对 LocalFree。
        unsafe {
            use windows::Win32::Foundation::{HLOCAL, LocalFree};
            let _ = LocalFree(Some(HLOCAL(self.0)));
        }
    }
}

#[cfg(windows)]
fn create_command_security_attributes() -> Result<SecurityDescriptorWrapper> {
    use windows::Win32::Security::Authorization::{
        ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
    };
    use windows::core::PCWSTR;

    let owner_sid = current_exe_owner_sid()?;
    let sddl = format!("D:(A;;GA;;;{owner_sid})(A;;GA;;;BA)(A;;GA;;;SY)(A;;GA;;;LS)");
    let sddl_wide: Vec<u16> = sddl.encode_utf16().chain(std::iter::once(0)).collect();
    let mut security_descriptor: *mut std::ffi::c_void = std::ptr::null_mut();

    // 命令管道访问限制给安装所有者、管理员、SYSTEM 和服务账号。
    unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            PCWSTR(sddl_wide.as_ptr()),
            SDDL_REVISION_1,
            std::ptr::addr_of_mut!(security_descriptor) as *mut _,
            None,
        )
        .context("Failed to create IPC security descriptor")?;
    }

    Ok(SecurityDescriptorWrapper(security_descriptor))
}

#[cfg(windows)]
fn current_exe_owner_sid() -> Result<String> {
    use std::os::windows::ffi::OsStrExt;
    use windows::Win32::Foundation::{HLOCAL, LocalFree};
    use windows::Win32::Security::Authorization::{
        ConvertSidToStringSidW, GetNamedSecurityInfoW, SE_FILE_OBJECT,
    };
    use windows::Win32::Security::{
        OBJECT_SECURITY_INFORMATION, OWNER_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR, PSID,
    };
    use windows::core::{PCWSTR, PWSTR};

    let exe = std::env::current_exe().context("Failed to get the service executable path")?;
    let path: Vec<u16> = exe
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let mut owner = PSID::default();
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    // 安全前提：path 是 NUL 结尾 UTF-16；返回的描述符用 LocalFree 释放。
    let error = unsafe {
        GetNamedSecurityInfoW(
            PCWSTR(path.as_ptr()),
            SE_FILE_OBJECT,
            OBJECT_SECURITY_INFORMATION(OWNER_SECURITY_INFORMATION.0),
            Some(&mut owner),
            None,
            None,
            None,
            &mut descriptor,
        )
    };
    if error.0 != 0 {
        return Err(std::io::Error::from_raw_os_error(error.0 as i32))
            .context("Failed to read the service executable owner");
    }

    let mut sid_text = PWSTR::null();
    // 安全前提：owner 来自 GetNamedSecurityInfoW；sid_text 由系统分配并用 LocalFree 释放。
    let sid = unsafe {
        ConvertSidToStringSidW(owner, &mut sid_text).context("Failed to convert owner SID")?;
        let text = sid_text.to_string().context("Failed to read owner SID")?;
        let _ = LocalFree(Some(HLOCAL(sid_text.0.cast())));
        let _ = LocalFree(Some(HLOCAL(descriptor.0)));
        text
    };
    Ok(sid)
}

#[cfg(windows)]
fn create_named_pipe_with_security(
    path: &str,
    is_first_instance: bool,
    security_descriptor: &SecurityDescriptorWrapper,
) -> Result<tokio::net::windows::named_pipe::NamedPipeServer> {
    use std::os::windows::io::RawHandle;
    use windows::Win32::Security::SECURITY_ATTRIBUTES;
    use windows::Win32::Storage::FileSystem::{
        FILE_FLAG_FIRST_PIPE_INSTANCE, FILE_FLAG_OVERLAPPED, PIPE_ACCESS_DUPLEX,
    };
    use windows::Win32::System::Pipes::{
        CreateNamedPipeW, PIPE_READMODE_BYTE, PIPE_TYPE_BYTE, PIPE_UNLIMITED_INSTANCES, PIPE_WAIT,
    };
    use windows::core::PCWSTR;

    let path_wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
    let security_attrs = SECURITY_ATTRIBUTES {
        nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: security_descriptor.0,
        bInheritHandle: false.into(),
    };
    let open_mode = if is_first_instance {
        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE
    } else {
        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED
    };

    // Named Pipe 需要自定义 ACL，因为 Tokio Builder 不暴露安全描述符。
    let handle = unsafe {
        CreateNamedPipeW(
            PCWSTR(path_wide.as_ptr()),
            open_mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            65536,
            65536,
            0,
            Some(&security_attrs as *const _),
        )
    };

    if handle.is_invalid() {
        bail!(
            "Failed to create Named Pipe: {}",
            std::io::Error::last_os_error()
        );
    }

    // Windows API 句柄移交给 Tokio；NamedPipeServer 从这里开始负责关闭。
    unsafe {
        tokio::net::windows::named_pipe::NamedPipeServer::from_raw_handle(handle.0 as RawHandle)
            .context("Failed to wrap Named Pipe")
    }
}
