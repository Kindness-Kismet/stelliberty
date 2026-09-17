namespace Stelliberty.Tray;

// 服务进程 spawn mihomo 所需的启动参数，仅供托盘宿主核心管理使用
public sealed record ServiceModeCoreHostRequest(
    string CorePath,
    string DataCoreDir,
    string ConfigPath);
