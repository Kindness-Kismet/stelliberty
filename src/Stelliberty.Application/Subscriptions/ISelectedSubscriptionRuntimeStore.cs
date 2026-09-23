using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISelectedSubscriptionRuntimeStore
{
    // 持久化订阅原文与运行时配置，供调试查看与后续读取；跨层只传内容。
    // effectiveConfigContent 是覆写与链式代理之后、规则覆写之前的基线，供规则页展示与校验。
    void Save(Subscription subscription, string originalContent, string effectiveConfigContent, string runtimeConfigContent);

    void SaveEmpty(string runtimeConfigContent);

    string ReadRuntimeConfig(string subscriptionId);

    void Delete(string subscriptionId);
}
