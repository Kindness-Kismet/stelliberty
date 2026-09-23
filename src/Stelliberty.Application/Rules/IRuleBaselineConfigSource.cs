namespace Stelliberty.Application.Rules;

public interface IRuleBaselineConfigSource
{
    // 返回覆写与链式代理之后、规则覆写之前的基线；尚未生成时返回 null，由调用方回退订阅原文。
    string? ReadBaseline(string subscriptionId);
}
