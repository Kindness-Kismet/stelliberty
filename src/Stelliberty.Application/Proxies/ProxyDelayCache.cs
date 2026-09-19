namespace Stelliberty.Application.Proxies;

// 配置会话变化后拒收旧测速结果；同名节点不能跨配置复用延迟。
public sealed class ProxyDelayCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProxyDelayPublication> _results = new(StringComparer.Ordinal);
    private string _scope = Guid.NewGuid().ToString("N");

    public string Scope
    {
        get { lock (_gate) return _scope; }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _scope = Guid.NewGuid().ToString("N");
            _results.Clear();
        }
    }

    public bool Publish(ProxyDelayPublication publication)
    {
        lock (_gate)
        {
            if (publication.Scope != _scope
                || (_results.TryGetValue(publication.ProxyName, out var previous)
                    && previous.CompletedAt > publication.CompletedAt))
            {
                return false;
            }

            _results[publication.ProxyName] = publication;
            return true;
        }
    }

    public IReadOnlyDictionary<string, int> GetDelays()
    {
        lock (_gate)
        {
            return _results.ToDictionary(item => item.Key, item => item.Value.Delay, StringComparer.Ordinal);
        }
    }
}
