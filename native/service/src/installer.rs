use std::io::Read;
use std::time::Duration;

use anyhow::{Context, Result, bail};

#[cfg(target_os = "linux")]
use crate::channel::linux_unit_path;
#[cfg(any(windows, target_os = "linux"))]
use crate::channel::service_name;
#[cfg(target_os = "macos")]
use crate::channel::{launchd_label, launchd_plist_path};
use crate::ipc::send_command;
use crate::protocol::{ServiceCommand, ServiceResponse};

pub fn install() -> Result<()> {
    #[cfg(windows)]
    {
        windows_install()?;
    }

    #[cfg(target_os = "linux")]
    {
        linux_install()?;
    }

    #[cfg(target_os = "macos")]
    {
        macos_install()?;
    }

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        bail!("Service installation is not supported on this platform yet");
    }

    println!("install ok");
    Ok(())
}

pub fn uninstall() -> Result<()> {
    #[cfg(windows)]
    {
        windows_uninstall()?;
    }

    #[cfg(target_os = "linux")]
    {
        linux_uninstall()?;
    }

    #[cfg(target_os = "macos")]
    {
        macos_uninstall()?;
    }

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        bail!("Service uninstall is not supported on this platform yet");
    }

    crate::core::cleanup_orphan_core_processes(None)
        .context("Failed to clean up leftover service cores")?;

    println!("uninstall ok");
    Ok(())
}

pub fn start() -> Result<()> {
    #[cfg(windows)]
    {
        windows_start()?;
    }

    #[cfg(target_os = "linux")]
    {
        command_success("systemctl", &["start", service_name()])?;
    }

    #[cfg(target_os = "macos")]
    {
        command_success("launchctl", &["bootstrap", "system", launchd_plist_path()])?;
    }

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        bail!("Service start is not supported on this platform yet");
    }

    println!("start ok");
    Ok(())
}

pub fn stop() -> Result<()> {
    #[cfg(windows)]
    {
        windows_stop()?;
    }

    #[cfg(target_os = "linux")]
    {
        command_success("systemctl", &["stop", service_name()])?;
    }

    #[cfg(target_os = "macos")]
    {
        command_success("launchctl", &["bootout", "system", launchd_plist_path()])?;
    }

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        bail!("Service stop is not supported on this platform yet");
    }

    println!("stop ok");
    Ok(())
}

pub fn print_status() -> Result<()> {
    match send_service_command(ServiceCommand::Status, Duration::from_secs(2)) {
        Ok(ServiceResponse::Status {
            service_name,
            version,
            uptime_seconds,
            last_heartbeat_seconds,
            core_state,
            core_pid,
            core_last_error,
        }) => {
            println!(
                "running service={service_name} version={version} uptime={uptime_seconds}s heartbeat={} core={} pid={}{}",
                last_heartbeat_seconds
                    .map(|seconds| format!("{seconds}s"))
                    .unwrap_or_else(|| "none".to_string()),
                core_state,
                core_pid
                    .map(|pid| pid.to_string())
                    .unwrap_or_else(|| "none".to_string()),
                core_last_error
                    .map(|error| format!(" error={error}"))
                    .unwrap_or_default()
            );
            Ok(())
        }
        Ok(other) => bail!("Unexpected status response: {other:?}"),
        Err(_) => print_platform_status(),
    }
}

pub fn heartbeat() -> Result<()> {
    match send_service_command(ServiceCommand::Heartbeat, Duration::from_secs(2))? {
        ServiceResponse::HeartbeatAck => {
            println!("heartbeat ok");
            Ok(())
        }
        other => bail!("Unexpected heartbeat response: {other:?}"),
    }
}

pub fn logs(lines: Option<usize>) -> Result<()> {
    let line_count = lines.unwrap_or_else(crate::logging::default_line_count);
    match send_service_command(
        ServiceCommand::Logs { lines: line_count },
        Duration::from_secs(2),
    ) {
        Ok(ServiceResponse::Logs { lines }) => {
            if lines.is_empty() {
                println!("no logs");
                return Ok(());
            }

            for line in lines {
                println!("{line}");
            }
            Ok(())
        }
        Ok(other) => bail!("Unexpected log response: {other:?}"),
        Err(e) => bail!("Service logs are unavailable: {e:#}"),
    }
}

pub fn start_core_from_stdin() -> Result<()> {
    let mut payload = String::new();
    std::io::stdin()
        .read_to_string(&mut payload)
        .context("Failed to read core startup parameters")?;
    let command: ServiceCommand =
        serde_json::from_str(&payload).context("Failed to parse core startup parameters")?;

    match send_service_command(command, Duration::from_secs(15))? {
        ServiceResponse::Success { message } => {
            println!("{}", message.unwrap_or_else(|| "core started".to_string()));
            Ok(())
        }
        other => bail!("Unexpected core start response: {other:?}"),
    }
}

pub fn stop_core() -> Result<()> {
    match send_service_command(ServiceCommand::StopCore, Duration::from_secs(15))? {
        ServiceResponse::Success { message } => {
            println!("{}", message.unwrap_or_else(|| "core stopped".to_string()));
            Ok(())
        }
        other => bail!("Unexpected core stop response: {other:?}"),
    }
}

pub fn apply_core_config_from_stdin() -> Result<()> {
    let mut payload = String::new();
    std::io::stdin()
        .read_to_string(&mut payload)
        .context("Failed to read core config parameters")?;
    let command: ServiceCommand =
        serde_json::from_str(&payload).context("Failed to parse core config parameters")?;

    match send_service_command(command, Duration::from_secs(30))? {
        ServiceResponse::CoreConfigApplied { mode, pid } => {
            // stdout 格式与应用侧约定：mode=<reload|restart> pid=<n>
            println!("mode={mode} pid={pid}");
            Ok(())
        }
        other => bail!("Unexpected core apply response: {other:?}"),
    }
}

pub fn restart_core() -> Result<()> {
    match send_service_command(ServiceCommand::RestartCore, Duration::from_secs(15))? {
        ServiceResponse::Success { message } => {
            println!(
                "{}",
                message.unwrap_or_else(|| "core restarted".to_string())
            );
            Ok(())
        }
        other => bail!("Unexpected core restart response: {other:?}"),
    }
}

pub fn shutdown() -> Result<()> {
    match send_service_command(ServiceCommand::Shutdown, Duration::from_secs(2))? {
        ServiceResponse::Success { message } => {
            println!("{}", message.unwrap_or_else(|| "shutdown ok".to_string()));
            Ok(())
        }
        other => bail!("Unexpected shutdown response: {other:?}"),
    }
}

fn send_service_command(command: ServiceCommand, timeout: Duration) -> Result<ServiceResponse> {
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .context("Failed to create the IPC runtime")?;
    runtime.block_on(send_command(command, timeout))
}

#[cfg(windows)]
fn windows_install() -> Result<()> {
    use std::ffi::OsString;
    use windows_service::service::{ServiceAccess, ServiceState};
    use windows_service::service_manager::{ServiceManager, ServiceManagerAccess};

    let manager = ServiceManager::local_computer(
        None::<&str>,
        ServiceManagerAccess::CONNECT | ServiceManagerAccess::CREATE_SERVICE,
    )
    .context("Failed to connect to Windows Service Manager; make sure the process is elevated")?;
    let binary_path =
        std::env::current_exe().context("Failed to get the service executable path")?;

    match manager.open_service(
        service_name(),
        ServiceAccess::QUERY_STATUS
            | ServiceAccess::STOP
            | ServiceAccess::START
            | ServiceAccess::CHANGE_CONFIG,
    ) {
        Ok(service) => {
            if service.query_status()?.current_state != ServiceState::Stopped {
                let _ = service.stop();
                wait_windows_state(&service, ServiceState::Stopped)?;
            }

            let service_info = windows_service_info(binary_path);
            service
                .change_config(&service_info)
                .context("Failed to update the Windows service configuration")?;
            println!("Service configuration updated; starting service");
            service
                .start(&[] as &[&OsString])
                .context("Failed to start the Windows service")?;
            return wait_windows_state(&service, ServiceState::Running);
        }
        Err(windows_service::Error::Winapi(error)) if error.raw_os_error() == Some(1060) => {}
        Err(error) => return Err(error).context("Failed to open the Windows service"),
    }

    let service_info = windows_service_info(binary_path);
    manager
        .create_service(
            &service_info,
            ServiceAccess::QUERY_STATUS | ServiceAccess::START,
        )
        .context("Failed to create the Windows service")?;

    windows_start()
}

#[cfg(windows)]
fn windows_service_info(binary_path: std::path::PathBuf) -> windows_service::service::ServiceInfo {
    use std::ffi::OsString;
    use windows_service::service::{
        ServiceErrorControl, ServiceInfo, ServiceStartType, ServiceType,
    };

    ServiceInfo {
        name: OsString::from(service_name()),
        display_name: OsString::from(service_name()),
        service_type: ServiceType::OWN_PROCESS,
        start_type: ServiceStartType::AutoStart,
        error_control: ServiceErrorControl::Normal,
        executable_path: binary_path,
        launch_arguments: vec![],
        dependencies: vec![],
        account_name: None,
        account_password: None,
    }
}

#[cfg(windows)]
fn windows_uninstall() -> Result<()> {
    use windows_service::service::{ServiceAccess, ServiceState};
    use windows_service::service_manager::{ServiceManager, ServiceManagerAccess};

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context(
            "Failed to connect to Windows Service Manager; make sure the process is elevated",
        )?;
    let service = match manager.open_service(
        service_name(),
        ServiceAccess::QUERY_STATUS | ServiceAccess::STOP | ServiceAccess::DELETE,
    ) {
        Ok(service) => service,
        Err(windows_service::Error::Winapi(error)) if error.raw_os_error() == Some(1060) => {
            println!("service not installed");
            return Ok(());
        }
        Err(error) => return Err(error).context("Failed to open the Windows service"),
    };

    if service.query_status()?.current_state != ServiceState::Stopped {
        let _ = service.stop();
        wait_windows_state(&service, ServiceState::Stopped)?;
    }

    service
        .delete()
        .context("Failed to delete the Windows service")
}

#[cfg(windows)]
fn windows_start() -> Result<()> {
    use std::ffi::OsString;
    use windows_service::service::{ServiceAccess, ServiceState};
    use windows_service::service_manager::{ServiceManager, ServiceManagerAccess};

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context(
            "Failed to connect to Windows Service Manager; make sure the process is elevated",
        )?;
    let service = manager
        .open_service(
            service_name(),
            ServiceAccess::QUERY_STATUS | ServiceAccess::START,
        )
        .context("Failed to open the Windows service; install it first")?;

    if service.query_status()?.current_state == ServiceState::Running {
        println!("service already running");
        return Ok(());
    }

    service
        .start(&[] as &[&OsString])
        .context("Failed to start the Windows service")?;
    wait_windows_state(&service, ServiceState::Running)
}

#[cfg(windows)]
fn windows_stop() -> Result<()> {
    use windows_service::service::{ServiceAccess, ServiceState};
    use windows_service::service_manager::{ServiceManager, ServiceManagerAccess};

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context(
            "Failed to connect to Windows Service Manager; make sure the process is elevated",
        )?;
    let service = manager
        .open_service(
            service_name(),
            ServiceAccess::QUERY_STATUS | ServiceAccess::STOP,
        )
        .context("Failed to open the Windows service")?;

    if service.query_status()?.current_state == ServiceState::Stopped {
        println!("service already stopped");
        return Ok(());
    }

    service
        .stop()
        .context("Failed to stop the Windows service")?;
    wait_windows_state(&service, ServiceState::Stopped)
}

#[cfg(windows)]
fn wait_windows_state(
    service: &windows_service::service::Service,
    target: windows_service::service::ServiceState,
) -> Result<()> {
    for _ in 0..40 {
        if service.query_status()?.current_state == target {
            return Ok(());
        }
        std::thread::sleep(Duration::from_millis(250));
    }

    bail!("Timed out waiting for service state: {target:?}")
}

#[cfg(target_os = "linux")]
fn linux_install() -> Result<()> {
    let _ = command_success("systemctl", &["stop", service_name()]);
    let binary_path =
        std::env::current_exe().context("Failed to get the service executable path")?;
    let (uid, gid) = linux_service_owner(&binary_path)?;
    let unit = format!(
        "[Unit]\nDescription={} Service\nAfter=network.target\n\n[Service]\nType=simple\nUser={uid}\nGroup={gid}\nExecStart={}\nRestart=on-failure\nRestartSec=5s\n\n[Install]\nWantedBy=multi-user.target\n",
        service_name(),
        binary_path.display(),
    );
    std::fs::write(linux_unit_path(), unit).context("Failed to write the systemd unit")?;
    command_success("systemctl", &["daemon-reload"])?;
    command_success("systemctl", &["enable", service_name()])?;
    command_success("systemctl", &["restart", service_name()])
}

#[cfg(target_os = "linux")]
fn linux_uninstall() -> Result<()> {
    let _ = command_success("systemctl", &["stop", service_name()]);
    let _ = command_success("systemctl", &["disable", service_name()]);
    let path = linux_unit_path();
    if std::path::Path::new(path).exists() {
        std::fs::remove_file(path).context("Failed to delete the systemd unit")?;
    }
    command_success("systemctl", &["daemon-reload"])
}

#[cfg(target_os = "linux")]
fn linux_service_owner(path: &std::path::Path) -> Result<(u32, u32)> {
    use std::os::unix::fs::MetadataExt;

    let metadata =
        std::fs::metadata(path).context("Failed to read the service executable owner")?;
    Ok((metadata.uid(), metadata.gid()))
}

#[cfg(target_os = "macos")]
fn macos_install() -> Result<()> {
    let _ = command_success("launchctl", &["bootout", "system", launchd_plist_path()]);
    let binary_path =
        std::env::current_exe().context("Failed to get the service executable path")?;
    // 服务及其核心需要 root 权限创建 TUN；IPC 仍按服务文件所有者授权。
    let plist = format!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n  <key>Label</key><string>{}</string>\n  <key>UserName</key><string>{}</string>\n  <key>GroupName</key><string>{}</string>\n  <key>ProgramArguments</key><array><string>{}</string></array>\n  <key>RunAtLoad</key><true/>\n  <key>KeepAlive</key><true/>\n</dict>\n</plist>\n",
        xml_escape(launchd_label()),
        "root",
        "wheel",
        xml_escape(&binary_path.display().to_string())
    );
    std::fs::write(launchd_plist_path(), plist).context("Failed to write the launchd plist")?;
    command_success("launchctl", &["bootstrap", "system", launchd_plist_path()])
}

#[cfg(target_os = "macos")]
fn macos_uninstall() -> Result<()> {
    let _ = command_success("launchctl", &["bootout", "system", launchd_plist_path()]);
    let path = launchd_plist_path();
    if std::path::Path::new(path).exists() {
        std::fs::remove_file(path).context("Failed to delete the launchd plist")?;
    }
    Ok(())
}

#[cfg(target_os = "macos")]
fn xml_escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn command_success(program: &str, args: &[&str]) -> Result<()> {
    let status = std::process::Command::new(program)
        .args(args)
        .status()
        .with_context(|| format!("Failed to run command: {program}"))?;
    if status.success() {
        return Ok(());
    }

    bail!("Command returned a failure: {program} {}", args.join(" "))
}

fn print_platform_status() -> Result<()> {
    #[cfg(windows)]
    {
        windows_print_status()
    }

    #[cfg(target_os = "linux")]
    {
        linux_print_status()
    }

    #[cfg(target_os = "macos")]
    {
        macos_print_status()
    }

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        println!("not-installed");
        Ok(())
    }
}

#[cfg(windows)]
fn windows_print_status() -> Result<()> {
    use windows_service::service::{ServiceAccess, ServiceState};
    use windows_service::service_manager::{ServiceManager, ServiceManagerAccess};

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context("Failed to connect to Windows Service Manager")?;
    let service = match manager.open_service(service_name(), ServiceAccess::QUERY_STATUS) {
        Ok(service) => service,
        Err(windows_service::Error::Winapi(error)) if error.raw_os_error() == Some(1060) => {
            println!("not-installed");
            return Ok(());
        }
        Err(error) => return Err(error).context("Failed to open the Windows service"),
    };

    let state = if service.query_status()?.current_state == ServiceState::Running {
        "running"
    } else {
        "stopped"
    };
    println!("{state} version={}", crate::service_version());
    Ok(())
}

#[cfg(target_os = "linux")]
fn linux_print_status() -> Result<()> {
    let unit_path = linux_unit_path();
    if !std::path::Path::new(unit_path).exists() {
        println!("not-installed");
        return Ok(());
    }

    let output = std::process::Command::new("systemctl")
        .args(["is-active", service_name()])
        .output()
        .context("Failed to query systemd service status")?;
    let state = if String::from_utf8_lossy(&output.stdout).trim() == "active" {
        "running"
    } else {
        "stopped"
    };
    println!("{state} version={}", crate::service_version());
    Ok(())
}

#[cfg(target_os = "macos")]
fn macos_print_status() -> Result<()> {
    let path = launchd_plist_path();
    if !std::path::Path::new(path).exists() {
        println!("not-installed");
        return Ok(());
    }

    let output = std::process::Command::new("launchctl")
        .args(["print", &format!("system/{}", launchd_label())])
        .output()
        .context("Failed to query launchd service status")?;
    let state = if output.status.success() {
        "running"
    } else {
        "stopped"
    };
    println!("{state} version={}", crate::service_version());
    Ok(())
}
