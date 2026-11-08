namespace Stelliberty.Desktop.Services;

// 服务命令管道 Status 响应的 data 段，字段名由 snake_case 策略映射；响应里其余字段无消费方，反序列化时忽略。
internal sealed record ServiceStatusPayload(
    string ServiceName,
    string CoreState,
    int? CorePid,
    string? CoreLastError);
