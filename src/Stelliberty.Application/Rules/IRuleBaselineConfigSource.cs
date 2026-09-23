namespace Stelliberty.Application.Rules;

public interface IRuleBaselineConfigSource
{
    // 返回覆写与链式代理之后、规则覆写之前的基线；尚未生成时返回 null，规则页转只读。
    string? ReadBaseline(string subscriptionId);
}
