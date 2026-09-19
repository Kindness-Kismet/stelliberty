namespace Stelliberty.Presentation.Proxies;

internal static class ProxyDelayPresentation
{
    // 节点卡片与托盘共用 300/500 毫秒分档，避免延迟色阶不一致。
    public static string GetLevel(int? delay) => delay switch
    {
        null => "delay-none",
        < 0 => "delay-bad",
        <= 300 => "delay-good",
        <= 500 => "delay-mid",
        _ => "delay-slow"
    };
}
