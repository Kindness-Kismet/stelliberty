namespace Stelliberty.Desktop.Services;

// 服务启动核心所需的参数，仅供桌面宿主核心管理使用。
public sealed record ServiceModeCoreHostRequest(
    string CorePath,
    string DataCoreDir,
    string ConfigPath);
