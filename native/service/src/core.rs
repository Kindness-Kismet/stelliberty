use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result, anyhow};
use hub::infra::core_api::{ApiError, CoreApiClient};
use hub::util::yaml_diff;
use serde::Serialize;
use tokio::sync::Mutex;
use tokio::task::JoinHandle;

use crate::channel::core_lock_prefix;
use crate::logging;

const CORE_LOCK_SUFFIX: &str = ".lock";
// 核心停止固定 5 秒；服务退出时锁等待也计入该预算。
const CORE_STOP_TIMEOUT: Duration = Duration::from_secs(5);

pub const APPLY_MODE_RELOAD: &str = "reload";
pub const APPLY_MODE_RESTART: &str = "restart";

// mihomo 控制器端点按平台取字段；缺失时不可重载
#[cfg(windows)]
const CONTROLLER_FIELD: &str = "external-controller-pipe";
#[cfg(not(windows))]
const CONTROLLER_FIELD: &str = "external-controller-unix";

#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum CoreState {
    Running,
    Stopping,
    Stopped,
    Crashed,
}

#[derive(Debug, Clone, Serialize)]
pub struct CoreStatus {
    pub state: CoreState,
    pub pid: Option<u32>,
    pub last_error: Option<String>,
}

#[derive(Clone)]
pub struct CoreManager {
    inner: Arc<Mutex<CoreInner>>,
    lifecycle: Arc<Mutex<()>>,
}

struct CoreInner {
    state: CoreState,
    child: Option<ChildHandle>,
    last_request: Option<StartCoreRequest>,
    // spawn 或重载时应用的配置内容，作为下次 apply 的 diff 基准
    last_applied_yaml: Option<serde_yaml_ng::Value>,
    last_error: Option<String>,
    restore_after_heartbeat: bool,
    restore_in_progress: bool,
    generation: u64,
    wait_task: Option<JoinHandle<()>>,
}

#[derive(Debug, Clone)]
pub struct StartCoreRequest {
    pub mihomo_path: String,
    pub config_path: String,
    pub data_core_dir: String,
}

impl CoreManager {
    pub fn new() -> Self {
        Self {
            inner: Arc::new(Mutex::new(CoreInner {
                state: CoreState::Stopped,
                child: None,
                last_request: None,
                last_applied_yaml: None,
                last_error: None,
                restore_after_heartbeat: false,
                restore_in_progress: false,
                generation: 0,
                wait_task: None,
            })),
            lifecycle: Arc::new(Mutex::new(())),
        }
    }

    pub async fn start(&self, request: StartCoreRequest) -> Result<u32> {
        let _guard = self.lifecycle.lock().await;
        self.start_locked(request).await
    }

    async fn start_locked(&self, request: StartCoreRequest) -> Result<u32> {
        validate_start_request(&request)?;
        // 记录 spawn 时刻的配置内容，作为后续重载的 diff 基准
        let applied_yaml = read_applied_yaml(&request.config_path);
        self.stop_with_restore_flag_locked(false, CORE_STOP_TIMEOUT)
            .await?;
        let killed = self.cleanup_orphan_cores_checked().await?;
        log_cleaned_orphan_cores(&killed);
        let child = spawn_child(
            Path::new(&request.mihomo_path),
            Path::new(&request.config_path),
            Path::new(&request.data_core_dir),
        )?;
        let pid = child.pid;
        let waiter = exit_waiter(&child)?;
        {
            let mut inner = self.inner.lock().await;
            inner.generation += 1;
            inner.state = CoreState::Running;
            inner.last_request = Some(request);
            inner.last_applied_yaml = applied_yaml;
            inner.last_error = None;
            inner.restore_after_heartbeat = false;
            inner.restore_in_progress = false;
            inner.child = Some(child);
            if let Some(task) = inner.wait_task.take() {
                task.abort();
            }
            let generation = inner.generation;
            let state = self.inner.clone();
            inner.wait_task = Some(tokio::spawn(async move {
                let code = waiter.await;
                let mut inner = state.lock().await;
                if inner.generation != generation {
                    return;
                }

                inner.child = None;
                inner.state = CoreState::Crashed;
                inner.last_error = Some(if code == u32::MAX {
                    "Core process exited unexpectedly with an unknown exit code".to_string()
                } else {
                    format!("Core process exited unexpectedly with exit code {code}")
                });
                logging::warn(
                    inner
                        .last_error
                        .as_deref()
                        .unwrap_or("Core process exited unexpectedly"),
                );
            }));
        }

        logging::info(format!("Core started by service: pid={pid}"));
        Ok(pid)
    }

    pub async fn restart(&self) -> Result<u32> {
        let _guard = self.lifecycle.lock().await;
        let request = {
            let inner = self.inner.lock().await;
            inner
                .last_request
                .clone()
                .ok_or_else(|| anyhow!("Core startup parameters are missing"))?
        };
        self.start_locked(request).await
    }

    // 应用新配置：启动期字段未变时优先重载，否则回退重启
    pub async fn apply_config(&self, config_path: String) -> Result<CoreApplyOutcome> {
        let _guard = self.lifecycle.lock().await;
        validate_config_path(&config_path)?;
        let yaml_text = std::fs::read_to_string(&config_path)
            .with_context(|| format!("Failed to read core config: {config_path}"))?;
        let new_yaml: serde_yaml_ng::Value =
            serde_yaml_ng::from_str(&yaml_text).context("Failed to parse core config yaml")?;

        if let Some(pid) = self.reloadable_pid(&new_yaml).await
            && let Some(pipe) = controller_endpoint(&new_yaml)
        {
            match self.reload_core(&pipe, &config_path).await {
                ReloadOutcome::Applied => {
                    let mut inner = self.inner.lock().await;
                    inner.last_applied_yaml = Some(new_yaml);
                    logging::info(format!("Core config reloaded by service: pid={pid}"));
                    return Ok(CoreApplyOutcome {
                        mode: APPLY_MODE_RELOAD,
                        pid,
                    });
                }
                // mihomo 拒绝候选配置时不能回退重启，否则会保留坏配置
                ReloadOutcome::Rejected(error) => return Err(error),
                ReloadOutcome::Failed(error) => {
                    logging::warn(format!(
                        "Core reload failed; falling back to restart: {error:#}"
                    ));
                }
            }
        }

        // 回退重启：复用已记录的启动参数，仅替换配置路径
        let mut request = {
            let inner = self.inner.lock().await;
            inner
                .last_request
                .clone()
                .ok_or_else(|| anyhow!("Core startup parameters are missing"))?
        };
        request.config_path = config_path;
        let pid = self.start_locked(request).await?;
        Ok(CoreApplyOutcome {
            mode: APPLY_MODE_RESTART,
            pid,
        })
    }

    // 仅当核心在运行且启动期字段无差异时才可重载
    async fn reloadable_pid(&self, new_yaml: &serde_yaml_ng::Value) -> Option<u32> {
        let inner = self.inner.lock().await;
        let pid = match (&inner.state, inner.child.as_ref()) {
            (CoreState::Running, Some(child)) => child.pid,
            _ => return None,
        };
        let unchanged = inner
            .last_applied_yaml
            .as_ref()
            .is_some_and(|prev| !yaml_diff::needs_restart(prev, new_yaml));
        unchanged.then_some(pid)
    }

    async fn reload_core(&self, pipe: &str, config_path: &str) -> ReloadOutcome {
        let client = CoreApiClient::new(pipe);
        match client.put_config_path(Path::new(config_path)).await {
            Ok(()) => {
                if let Err(error) = client.close_all_connections().await {
                    logging::warn(format!(
                        "Failed to close core connections after reload: {error:#}"
                    ));
                }
                ReloadOutcome::Applied
            }
            Err(error) => match error.downcast_ref::<ApiError>() {
                Some(ApiError::ConfigRejected(_)) => ReloadOutcome::Rejected(error),
                _ => ReloadOutcome::Failed(error),
            },
        }
    }

    pub async fn stop(&self) -> Result<()> {
        let _guard = self.lifecycle.lock().await;
        self.stop_with_restore_flag_locked(false, CORE_STOP_TIMEOUT)
            .await
    }

    pub async fn stop_for_service_shutdown(&self) -> Result<()> {
        let deadline = tokio::time::Instant::now() + CORE_STOP_TIMEOUT;
        let _guard = tokio::time::timeout_at(deadline, self.lifecycle.lock())
            .await
            .map_err(|_| anyhow!("Core stop timed out waiting for another lifecycle operation"))?;
        let remaining = deadline.saturating_duration_since(tokio::time::Instant::now());
        self.stop_with_restore_flag_locked(false, remaining).await
    }

    async fn stop_with_restore_flag_locked(
        &self,
        restore_after_heartbeat: bool,
        timeout: Duration,
    ) -> Result<()> {
        let (child, task, generation) = {
            let mut inner = self.inner.lock().await;
            inner.generation += 1;
            inner.state = CoreState::Stopping;
            inner.last_error = None;
            inner.restore_after_heartbeat = false;
            inner.restore_in_progress = false;
            (inner.child.take(), inner.wait_task.take(), inner.generation)
        };

        if let Some(task) = task {
            task.abort();
        }

        if let Some(child) = child {
            shutdown_child(child, timeout).await?;
            logging::info("Core stopped by service");
        }

        let mut inner = self.inner.lock().await;
        if inner.generation == generation {
            inner.state = CoreState::Stopped;
            inner.last_applied_yaml = None;
            inner.restore_after_heartbeat = restore_after_heartbeat && inner.last_request.is_some();
            inner.restore_in_progress = false;
        }
        Ok(())
    }

    pub async fn stop_for_heartbeat_timeout(&self) {
        let _guard = self.lifecycle.lock().await;
        let had_child = self.status().await.pid.is_some();
        if let Err(error) = self
            .stop_with_restore_flag_locked(had_child, CORE_STOP_TIMEOUT)
            .await
        {
            logging::warn(format!(
                "Failed to stop core after heartbeat timeout: {error:#}"
            ));
        } else if had_child {
            logging::warn("Client heartbeat timed out; core has been stopped");
        }
    }

    pub async fn cleanup_orphan_cores(&self) {
        match self.cleanup_orphan_cores_checked().await {
            Ok(killed) => log_cleaned_orphan_cores(&killed),
            Err(error) => logging::warn(format!(
                "Failed to clean up orphaned core processes: {error:#}"
            )),
        }
    }

    async fn cleanup_orphan_cores_checked(&self) -> Result<Vec<u32>> {
        let excluded_pid = self.status().await.pid;
        cleanup_orphan_core_processes(excluded_pid)
    }

    pub async fn restore_if_needed(&self) -> Result<bool> {
        let _guard = self.lifecycle.lock().await;
        let request = {
            let mut inner = self.inner.lock().await;
            if !inner.restore_after_heartbeat
                || inner.restore_in_progress
                || !matches!(inner.state, CoreState::Stopped | CoreState::Crashed)
            {
                return Ok(false);
            }
            let Some(request) = inner.last_request.clone() else {
                inner.restore_after_heartbeat = false;
                return Ok(false);
            };
            inner.restore_in_progress = true;
            request
        };

        match self.start_locked(request).await {
            Ok(_) => Ok(true),
            Err(error) => {
                let mut inner = self.inner.lock().await;
                inner.restore_after_heartbeat = true;
                inner.restore_in_progress = false;
                Err(error)
            }
        }
    }

    pub async fn status(&self) -> CoreStatus {
        let inner = self.inner.lock().await;
        CoreStatus {
            state: inner.state,
            pid: inner.child.as_ref().map(|child| child.pid),
            last_error: inner.last_error.clone(),
        }
    }
}

#[derive(Debug, Clone, Copy)]
pub struct CoreApplyOutcome {
    pub mode: &'static str,
    pub pid: u32,
}

enum ReloadOutcome {
    Applied,
    // 4xx 被拒：候选配置有误，调用方不得回退重启
    Rejected(anyhow::Error),
    Failed(anyhow::Error),
}

fn controller_endpoint(yaml: &serde_yaml_ng::Value) -> Option<String> {
    let key = serde_yaml_ng::Value::String(CONTROLLER_FIELD.to_string());
    match yaml {
        serde_yaml_ng::Value::Mapping(map) => map.get(&key)?.as_str().map(str::to_owned),
        _ => None,
    }
}

// 读不到仅降级为下次 apply 走重启，不阻塞启动
fn read_applied_yaml(config_path: &str) -> Option<serde_yaml_ng::Value> {
    let text = match std::fs::read_to_string(config_path) {
        Ok(text) => text,
        Err(error) => {
            logging::warn(format!("Failed to read applied core config: {error:#}"));
            return None;
        }
    };
    match serde_yaml_ng::from_str(&text) {
        Ok(yaml) => Some(yaml),
        Err(error) => {
            logging::warn(format!("Failed to parse applied core config: {error}"));
            None
        }
    }
}

fn validate_start_request(request: &StartCoreRequest) -> Result<()> {
    let allowed = AllowedCorePaths::resolve()?;
    let mihomo_path = canonical_file(&request.mihomo_path, "core executable")?;
    let data_core_dir = canonical_dir(&request.data_core_dir, "core data directory")?;

    if !same_path(&mihomo_path, &allowed.mihomo_path) {
        return Err(anyhow!("Core executable path is outside the allowed area"));
    }

    if !same_path(&data_core_dir, &allowed.core_dir) {
        return Err(anyhow!("Core data directory is outside the allowed area"));
    }

    validate_config_path(&request.config_path)?;

    Ok(())
}

fn validate_config_path(config_path: &str) -> Result<()> {
    let allowed = AllowedCorePaths::resolve()?;
    let canonical = canonical_file(config_path, "core config")?;
    // 配置必须位于 mihomo home 内，否则重载会被 mihomo 的 SAFE_PATHS 拒绝
    if !canonical.starts_with(&allowed.core_dir) {
        return Err(anyhow!("Core config path is outside the allowed area"));
    }

    Ok(())
}

struct AllowedCorePaths {
    core_dir: PathBuf,
    mihomo_path: PathBuf,
}

impl AllowedCorePaths {
    fn resolve() -> Result<Self> {
        let data_root = service_data_root()?;
        let core_dir = data_root
            .join("core")
            .canonicalize()
            .context("Core directory does not exist")?;
        let mihomo_path = core_dir
            .join(core_binary_name())
            .canonicalize()
            .context("Core executable does not exist")?;
        Ok(Self {
            core_dir,
            mihomo_path,
        })
    }
}

fn service_data_root() -> Result<PathBuf> {
    let exe = std::env::current_exe().context("Failed to get the service executable path")?;
    let data_root = exe
        .ancestors()
        .find(|path| {
            path.file_name()
                .and_then(|name| name.to_str())
                .is_some_and(|name| name.eq_ignore_ascii_case("data"))
        })
        .ok_or_else(|| anyhow!("Service executable is not under the app data directory"))?;
    data_root
        .canonicalize()
        .context("Failed to resolve the app data directory")
}

fn canonical_file(path: &str, label: &str) -> Result<PathBuf> {
    let path = Path::new(path)
        .canonicalize()
        .with_context(|| format!("{label} does not exist"))?;
    if path.is_file() {
        return Ok(path);
    }

    Err(anyhow!("{label} is not a file"))
}

fn canonical_dir(path: &str, label: &str) -> Result<PathBuf> {
    let path = Path::new(path)
        .canonicalize()
        .with_context(|| format!("{label} does not exist"))?;
    if path.is_dir() {
        return Ok(path);
    }

    Err(anyhow!("{label} is not a directory"))
}

fn core_binary_name() -> &'static str {
    if cfg!(windows) {
        "clash-mihomo-core.exe"
    } else {
        "clash-mihomo-core"
    }
}

fn same_path(left: &Path, right: &Path) -> bool {
    if cfg!(windows) {
        left.as_os_str()
            .to_string_lossy()
            .eq_ignore_ascii_case(&right.as_os_str().to_string_lossy())
    } else {
        left == right
    }
}

#[derive(Debug)]
struct ChildHandle {
    pid: u32,
    lock_path: PathBuf,
    #[cfg(windows)]
    inner: windows_impl::WindowsChild,
    #[cfg(unix)]
    inner: unix_impl::UnixChild,
}

fn spawn_child(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
    #[cfg(windows)]
    {
        windows_impl::spawn(binary, yaml_path, data_core_dir)
    }
    #[cfg(unix)]
    {
        unix_impl::spawn(binary, yaml_path, data_core_dir)
    }
}

async fn shutdown_child(child: ChildHandle, timeout: Duration) -> Result<()> {
    #[cfg(windows)]
    {
        windows_impl::shutdown(child, timeout).await
    }
    #[cfg(unix)]
    {
        unix_impl::shutdown(child, timeout).await
    }
}

fn exit_waiter(
    child: &ChildHandle,
) -> Result<std::pin::Pin<Box<dyn std::future::Future<Output = u32> + Send>>> {
    #[cfg(windows)]
    {
        windows_impl::exit_waiter(child)
    }
    #[cfg(unix)]
    {
        unix_impl::exit_waiter(child)
    }
}

pub(crate) fn cleanup_orphan_core_processes(excluded_pid: Option<u32>) -> Result<Vec<u32>> {
    let current_pid = std::process::id();
    let mut killed = Vec::new();
    let mut failed = Vec::new();
    for (pid, lock_path) in list_core_locks()? {
        if pid == current_pid || Some(pid) == excluded_pid {
            continue;
        }

        match terminate_process(pid) {
            Ok(true) => {
                killed.push(pid);
                remove_core_lock(&lock_path);
            }
            Ok(false) => remove_core_lock(&lock_path),
            Err(error) => failed.push(format!("pid={pid}: {error:#}")),
        }
    }

    if !failed.is_empty() {
        return Err(anyhow!(
            "Some orphaned core processes could not be cleaned up: {}",
            failed.join("; ")
        ));
    }

    Ok(killed)
}

fn format_pids(process_ids: &[u32]) -> String {
    process_ids
        .iter()
        .map(u32::to_string)
        .collect::<Vec<_>>()
        .join(",")
}

fn log_cleaned_orphan_cores(killed: &[u32]) {
    if !killed.is_empty() {
        logging::warn(format!(
            "Cleaned up orphaned core processes: pid={}",
            format_pids(killed)
        ));
    }
}

fn list_core_locks() -> Result<Vec<(u32, PathBuf)>> {
    let service_dir = service_lock_dir()?;
    if !service_dir.exists() {
        return Ok(Vec::new());
    }

    let mut locks = Vec::new();
    for entry in std::fs::read_dir(service_dir).context("Failed to read service core locks")? {
        let entry = entry?;
        let path = entry.path();
        if !path.is_file() {
            continue;
        }

        let Some(file_name) = path.file_name().and_then(|value| value.to_str()) else {
            continue;
        };
        let Some(pid_text) = file_name
            .strip_prefix(core_lock_prefix())
            .and_then(|value| value.strip_suffix(CORE_LOCK_SUFFIX))
        else {
            continue;
        };
        let Ok(pid) = pid_text.parse::<u32>() else {
            continue;
        };
        locks.push((pid, path));
    }

    Ok(locks)
}

fn create_core_lock(pid: u32) -> Result<PathBuf> {
    let service_dir = service_lock_dir()?;
    std::fs::create_dir_all(&service_dir)
        .context("Failed to create the service core lock directory")?;
    let lock_path = service_dir.join(format!("{}{pid}{CORE_LOCK_SUFFIX}", core_lock_prefix()));
    std::fs::write(&lock_path, format!("pid={pid}\n"))
        .context("Failed to write the service core lock")?;
    Ok(lock_path)
}

fn remove_core_lock(lock_path: &Path) {
    let _ = std::fs::remove_file(lock_path);
}

fn service_lock_dir() -> Result<PathBuf> {
    Ok(service_data_root()?.join("service"))
}

#[cfg(windows)]
fn terminate_process(pid: u32) -> Result<bool> {
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::System::Threading::{OpenProcess, PROCESS_TERMINATE, TerminateProcess};

    let process = match unsafe { OpenProcess(PROCESS_TERMINATE, false, pid) } {
        Ok(handle) => handle,
        Err(error) if is_process_not_found_error(&error) => return Ok(false),
        Err(error) => {
            return Err(anyhow!(
                "Failed to open orphaned core process pid={pid}: {error}"
            ));
        }
    };

    let result = unsafe { TerminateProcess(process, 1) };
    unsafe {
        let _ = CloseHandle(process);
    }
    result
        .map(|_| true)
        .map_err(|error| anyhow!("Failed to stop orphaned core pid={pid}: {error}"))
}

#[cfg(windows)]
fn is_process_not_found_error(error: &windows::core::Error) -> bool {
    (error.code().0 as u32) & 0xFFFF == 87
}

#[cfg(unix)]
fn terminate_process(pid: u32) -> Result<bool> {
    let result = unsafe { libc::kill(pid as libc::pid_t, libc::SIGKILL) };
    if result == 0 {
        return Ok(true);
    }

    if !process_exists(pid) {
        return Ok(false);
    }

    Err(anyhow!("Failed to stop orphaned core pid={pid}"))
}

#[cfg(unix)]
fn process_exists(pid: u32) -> bool {
    let result = unsafe { libc::kill(pid as libc::pid_t, 0) };
    if result == 0 {
        return true;
    }

    std::io::Error::last_os_error().raw_os_error() == Some(libc::EPERM)
}

#[cfg(windows)]
mod windows_impl {
    use super::*;
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use std::sync::Mutex as StdMutex;
    use std::time::Instant;
    use windows::Win32::Foundation::{
        CloseHandle, DUPLICATE_SAME_ACCESS, DuplicateHandle, HANDLE, WAIT_FAILED, WAIT_OBJECT_0,
        WAIT_TIMEOUT,
    };
    use windows::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JobObjectExtendedLimitInformation,
        SetInformationJobObject,
    };
    use windows::Win32::System::Threading::{
        CREATE_NO_WINDOW, CREATE_SUSPENDED, CreateProcessW, GetCurrentProcess, GetExitCodeProcess,
        PROCESS_INFORMATION, ResumeThread, STARTF_USESHOWWINDOW, STARTUPINFOW, TerminateProcess,
        WaitForSingleObject,
    };
    use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;
    use windows::core::{PCWSTR, PWSTR};

    #[derive(Debug)]
    pub struct WindowsChild {
        process: StdMutex<Option<isize>>,
        job: StdMutex<Option<isize>>,
    }

    fn handle_to_isize(handle: HANDLE) -> isize {
        handle.0 as isize
    }

    fn isize_to_handle(value: isize) -> HANDLE {
        HANDLE(value as *mut std::ffi::c_void)
    }

    fn to_wide(value: &OsStr) -> Vec<u16> {
        value.encode_wide().chain(std::iter::once(0)).collect()
    }

    fn quote_arg(value: &str) -> String {
        if !value.is_empty() && !value.contains([' ', '\t', '"']) {
            return value.to_string();
        }

        let mut result = String::with_capacity(value.len() + 2);
        result.push('"');
        let mut backslashes = 0usize;
        for ch in value.chars() {
            match ch {
                '\\' => backslashes += 1,
                '"' => {
                    result.push_str(&"\\".repeat(backslashes * 2 + 1));
                    result.push('"');
                    backslashes = 0;
                }
                _ => {
                    result.push_str(&"\\".repeat(backslashes));
                    result.push(ch);
                    backslashes = 0;
                }
            }
        }
        result.push_str(&"\\".repeat(backslashes * 2));
        result.push('"');
        result
    }

    pub fn spawn(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
        let command_line = format!(
            "{binary} -f {yaml} -d {data}",
            binary = quote_arg(&binary.to_string_lossy()),
            yaml = quote_arg(&yaml_path.to_string_lossy()),
            data = quote_arg(&data_core_dir.to_string_lossy()),
        );
        let mut cmd_wide = to_wide(OsStr::new(&command_line));
        let cmd_ptr = PWSTR(cmd_wide.as_mut_ptr());

        // 安全前提：创建匿名 Job Object 没有输入；失败转为 Err。
        let job_handle = unsafe { CreateJobObjectW(None, PCWSTR::null()) }
            .map_err(|error| anyhow!("Failed to create Job Object: {error}"))?;
        let mut job_info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
        job_info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        // 安全前提：job_info 指针和大小匹配；Windows 不会持有指针。
        let info_set = unsafe {
            SetInformationJobObject(
                job_handle,
                JobObjectExtendedLimitInformation,
                &job_info as *const _ as *const _,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        };
        if info_set.is_err() {
            // 安全前提：job_handle 已在本函数中成功创建。
            unsafe {
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to set Job Object information"));
        }

        let mut startup_info: STARTUPINFOW = unsafe { std::mem::zeroed() };
        startup_info.cb = std::mem::size_of::<STARTUPINFOW>() as u32;
        startup_info.dwFlags = STARTF_USESHOWWINDOW;
        startup_info.wShowWindow = SW_HIDE.0 as u16;
        let mut process_info: PROCESS_INFORMATION = unsafe { std::mem::zeroed() };

        // 安全前提：cmd_ptr 指向本地可写 UTF-16 缓冲区，CreateProcessW 不会持有。
        let created = unsafe {
            CreateProcessW(
                PCWSTR::null(),
                Some(cmd_ptr),
                None,
                None,
                false,
                CREATE_NO_WINDOW | CREATE_SUSPENDED,
                None,
                PCWSTR::null(),
                &startup_info,
                &mut process_info,
            )
        };
        if created.is_err() {
            // 安全前提：job_handle 已在本函数中成功创建。
            unsafe {
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to create process"));
        }

        // 安全前提：失败路径会先终止挂起进程，再关闭已创建句柄。
        if unsafe { AssignProcessToJobObject(job_handle, process_info.hProcess) }.is_err() {
            unsafe {
                let _ = TerminateProcess(process_info.hProcess, 1);
                let _ = CloseHandle(process_info.hProcess);
                let _ = CloseHandle(process_info.hThread);
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to assign process to Job Object"));
        }

        let lock_path = match create_core_lock(process_info.dwProcessId) {
            Ok(lock_path) => lock_path,
            Err(error) => {
                unsafe {
                    let _ = TerminateProcess(process_info.hProcess, 1);
                    let _ = CloseHandle(process_info.hProcess);
                    let _ = CloseHandle(process_info.hThread);
                    let _ = CloseHandle(job_handle);
                }
                return Err(error);
            }
        };

        if unsafe { ResumeThread(process_info.hThread) } == u32::MAX {
            unsafe {
                let _ = TerminateProcess(process_info.hProcess, 1);
                let _ = CloseHandle(process_info.hProcess);
                let _ = CloseHandle(process_info.hThread);
                let _ = CloseHandle(job_handle);
            }
            remove_core_lock(&lock_path);
            return Err(anyhow!("Failed to resume the process thread"));
        }

        // 安全前提：主线程句柄未使用；进程和 Job 句柄移入 ChildHandle。
        unsafe {
            let _ = CloseHandle(process_info.hThread);
        }

        Ok(ChildHandle {
            pid: process_info.dwProcessId,
            lock_path,
            inner: WindowsChild {
                process: StdMutex::new(Some(handle_to_isize(process_info.hProcess))),
                job: StdMutex::new(Some(handle_to_isize(job_handle))),
            },
        })
    }

    pub fn exit_waiter(
        child: &ChildHandle,
    ) -> Result<std::pin::Pin<Box<dyn std::future::Future<Output = u32> + Send>>> {
        let process = child
            .inner
            .process
            .lock()
            .map_err(|_| anyhow!("process lock is poisoned"))?
            .ok_or_else(|| anyhow!("Child process handle has been released"))?;
        let mut duplicate = HANDLE::default();
        // 安全前提：复制当前持有的有效进程句柄，仅供后台等待器使用。
        unsafe {
            DuplicateHandle(
                GetCurrentProcess(),
                isize_to_handle(process),
                GetCurrentProcess(),
                &mut duplicate,
                0,
                false,
                DUPLICATE_SAME_ACCESS,
            )
        }
        .map_err(|error| anyhow!("Failed to duplicate process handle: {error}"))?;
        let duplicate = handle_to_isize(duplicate);
        let lock_path = child.lock_path.clone();
        Ok(Box::pin(async move {
            tokio::task::spawn_blocking(move || unsafe {
                let _ = WaitForSingleObject(isize_to_handle(duplicate), 0xFFFF_FFFF);
                let mut code = 0u32;
                let _ = GetExitCodeProcess(isize_to_handle(duplicate), &mut code);
                let _ = CloseHandle(isize_to_handle(duplicate));
                remove_core_lock(&lock_path);
                code
            })
            .await
            .unwrap_or(u32::MAX)
        }))
    }

    pub async fn shutdown(child: ChildHandle, timeout: Duration) -> Result<()> {
        let pid = child.pid;
        let started = Instant::now();
        let inner = child.inner;
        let lock_path = child.lock_path;
        logging::info(format!(
            "Core process stop started: pid={pid} wait_ms={} method=job-close",
            timeout.as_millis()
        ));
        let result = tokio::task::spawn_blocking(move || -> Result<()> {
            let job = inner
                .job
                .lock()
                .map_err(|_| anyhow!("job lock is poisoned"))?
                .take();
            let process = inner
                .process
                .lock()
                .map_err(|_| anyhow!("process lock is poisoned"))?
                .take();

            let mut failures = Vec::new();
            let mut has_exited = false;
            // 安全前提：整数句柄只来自本模块持有的 Win32 句柄，等待结束后各关闭一次。
            unsafe {
                if let Some(job) = job {
                    if let Err(error) = CloseHandle(isize_to_handle(job)) {
                        failures.push(format!("Failed to close core Job Object: {error}"));
                    }
                }
                if let Some(process) = process {
                    let wait = WaitForSingleObject(isize_to_handle(process), timeout.as_millis() as u32);
                    if wait == WAIT_OBJECT_0 {
                        has_exited = true;
                        let mut exit_code = 0u32;
                        match GetExitCodeProcess(isize_to_handle(process), &mut exit_code) {
                            Ok(()) => logging::info(format!(
                                "Core process exit confirmed: pid={pid} elapsed_ms={} exit_code=0x{exit_code:08X}",
                                started.elapsed().as_millis()
                            )),
                            Err(error) => logging::warn(format!(
                                "Core process exit confirmed: pid={pid} elapsed_ms={} exit_code=unavailable error={error}",
                                started.elapsed().as_millis()
                            )),
                        }
                    } else if wait == WAIT_TIMEOUT {
                        failures.push("Timed out waiting for core to exit".to_string());
                    } else if wait == WAIT_FAILED {
                        failures.push(format!(
                            "Failed to wait for core to exit: {}",
                            windows::core::Error::from_thread()
                        ));
                    } else {
                        failures.push(format!("Unexpected core wait result: 0x{:08X}", wait.0));
                    }
                    if let Err(error) = CloseHandle(isize_to_handle(process)) {
                        failures.push(format!("Failed to close core process handle: {error}"));
                    }
                } else {
                    failures.push("Core process handle is unavailable; exit cannot be confirmed".to_string());
                }
            }
            if has_exited {
                remove_core_lock(&lock_path);
            }
            if !failures.is_empty() {
                return Err(anyhow!(failures.join("; ")));
            }
            Ok(())
        })
        .await
        .context("Core shutdown background task failed")
        .and_then(|result| result);
        match &result {
            Ok(()) => logging::info(format!(
                "Core process stop completed: pid={pid} wait_ms={} elapsed_ms={}",
                timeout.as_millis(),
                started.elapsed().as_millis()
            )),
            Err(error) => logging::error(format!(
                "Core process stop failed: pid={pid} wait_ms={} elapsed_ms={} error={error:#}",
                timeout.as_millis(),
                started.elapsed().as_millis()
            )),
        }
        result
    }
}

#[cfg(unix)]
mod unix_impl {
    use super::*;
    use std::process::Stdio;
    use std::sync::Arc;
    use tokio::process::Command;
    use tokio::sync::{Notify, watch};

    #[derive(Debug)]
    pub struct UnixChild {
        kill: Arc<Notify>,
        exited: watch::Receiver<bool>,
    }

    pub fn spawn(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
        let mut child = Command::new(binary)
            .arg("-f")
            .arg(yaml_path)
            .arg("-d")
            .arg(data_core_dir)
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .context("Failed to start the core")?;
        let pid = child
            .id()
            .ok_or_else(|| anyhow!("Unable to get core PID"))?;
        let lock_path = match create_core_lock(pid) {
            Ok(lock_path) => lock_path,
            Err(error) => {
                let _ = child.start_kill();
                return Err(error);
            }
        };
        let kill = Arc::new(Notify::new());
        let (exited_tx, exited_rx) = watch::channel(false);
        let kill_listener = kill.clone();
        let reaper_lock_path = lock_path.clone();
        tokio::spawn(async move {
            tokio::select! {
                _ = child.wait() => {}
                _ = kill_listener.notified() => {
                    let _ = child.start_kill();
                    let _ = child.wait().await;
                }
            }
            remove_core_lock(&reaper_lock_path);
            let _ = exited_tx.send(true);
        });
        Ok(ChildHandle {
            pid,
            lock_path,
            inner: UnixChild {
                kill,
                exited: exited_rx,
            },
        })
    }

    pub fn exit_waiter(
        child: &ChildHandle,
    ) -> Result<std::pin::Pin<Box<dyn std::future::Future<Output = u32> + Send>>> {
        let mut exited = child.inner.exited.clone();
        Ok(Box::pin(async move {
            let _ = exited.wait_for(|&done| done).await;
            u32::MAX
        }))
    }

    pub async fn shutdown(child: ChildHandle, timeout: Duration) -> Result<()> {
        let lock_path = child.lock_path;
        child.inner.kill.notify_one();
        let mut exited = child.inner.exited;
        tokio::time::timeout(timeout, exited.wait_for(|&done| done))
            .await
            .context("Core stop timed out")?
            .context("Core exit-status channel closed")?;
        remove_core_lock(&lock_path);
        Ok(())
    }
}
