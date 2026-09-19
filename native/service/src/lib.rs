// 服务模式入口只路由命令；平台设置和 IPC 保持模块化。

pub mod channel;
mod core;
mod heartbeat;
mod installer;
mod ipc;
mod logging;
mod protocol;
mod service;

use std::io::IsTerminal;

use anyhow::{Result, bail};

const HELP_COMMANDS: &str = "\
Commands:
  foreground           Run the service in the foreground
  install              Install and start the system service
  uninstall            Stop and uninstall the system service
  start                Start the system service
  stop                 Stop the system service
  status               Print service status
  heartbeat            Send a client heartbeat
  start-core           Read startup parameters from stdin and start the core
  apply-core-config    Read the config path from stdin and apply the core config
  stop-core            Stop the core hosted by the service
  restart-core         Restart the core hosted by the service
  shutdown             Ask the service to exit
  logs [lines]         Print service logs
  version              Print version
  help                 Print help

Options:
  -h, --help           Print help
  -v, --version        Print version";

pub fn run() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    let command_name = args.get(1).map(String::as_str).unwrap_or("service");
    logging::info(format!("Running service command: {command_name}"));
    let result = match args.get(1).map(String::as_str) {
        None if should_print_help_without_command() => {
            print_help();
            Ok(())
        }
        None => run_without_command(),
        Some("foreground") => service::run_foreground(),
        Some("install") => installer::install(),
        Some("uninstall") => installer::uninstall(),
        Some("start") => installer::start(),
        Some("stop") => installer::stop(),
        Some("status") => installer::print_status(),
        Some("heartbeat") => installer::heartbeat(),
        Some("start-core") => installer::start_core_from_stdin(),
        Some("apply-core-config") => installer::apply_core_config_from_stdin(),
        Some("stop-core") => installer::stop_core(),
        Some("restart-core") => installer::restart_core(),
        Some("shutdown") => installer::shutdown(),
        Some("logs") => installer::logs(parse_log_lines(args.get(2))?),
        Some("help") | Some("-h") | Some("--help") => {
            print_help();
            Ok(())
        }
        Some("version") | Some("-v") | Some("--version") => {
            println!(
                "{} {}",
                crate::channel::service_binary_name(),
                service_version()
            );
            Ok(())
        }
        Some(other) => bail!("Unknown command: {other}"),
    };
    match &result {
        Ok(_) => logging::info(format!("Service command completed: {command_name}")),
        Err(error) => logging::error(format!("Service command failed: {command_name}: {error:#}")),
    }
    result
}

fn should_print_help_without_command() -> bool {
    std::io::stdout().is_terminal() || std::io::stderr().is_terminal()
}

fn run_without_command() -> Result<()> {
    match service::run_as_service() {
        Ok(_) => Ok(()),
        Err(error) if cfg!(windows) && format!("{error:#}").contains("os error 1063") => {
            print_help();
            Ok(())
        }
        Err(error) => Err(error),
    }
}

fn print_help() {
    println!(
        "{} {}",
        crate::channel::service_binary_name(),
        service_version()
    );
    println!(
        "Usage:\n  {} [command]\n\n{HELP_COMMANDS}",
        crate::channel::service_binary_name()
    );
}

// 服务版本固定为 crate 版本，不随 App 版本漂移。
pub(crate) fn service_version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}

fn parse_log_lines(value: Option<&String>) -> Result<Option<usize>> {
    match value {
        Some(text) => {
            Ok(Some(text.parse().map_err(|_| {
                anyhow::anyhow!("Invalid log line count: {text}")
            })?))
        }
        None => Ok(None),
    }
}
