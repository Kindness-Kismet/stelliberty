using Stelliberty.Domain.Subscriptions;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionProviderParser
{
    public IReadOnlyList<SubscriptionProvider> Parse(string content)
    {
        var root = LoadRoot(content);
        if (root is null)
        {
            return [];
        }

        return ParseSection(root, "proxy-providers", "proxy")
            .Concat(ParseSection(root, "rule-providers", "rule"))
            .ToList();
    }

    private static YamlMappingNode? LoadRoot(string content)
    {
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(content));
            return yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode as YamlMappingNode;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<SubscriptionProvider> ParseSection(YamlMappingNode root, string sectionName, string providerType)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(sectionName), out var sectionNode) || sectionNode is not YamlMappingNode section)
        {
            return [];
        }

        return section.Children
            .Where(item => item.Key is YamlScalarNode && item.Value is YamlMappingNode)
            .Select(item => ParseProvider(((YamlScalarNode)item.Key).Value ?? string.Empty, providerType, ResolveMergedMapping((YamlMappingNode)item.Value)))
            .ToList();
    }

    private static YamlMappingNode ResolveMergedMapping(YamlMappingNode node)
    {
        // YAML 别名可能成环；visiting 记录展开栈。
        return ResolveMergedMapping(node, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance));
    }

    private static YamlMappingNode ResolveMergedMapping(YamlMappingNode node, HashSet<YamlNode> visiting)
    {
        if (!visiting.Add(node))
        {
            return new YamlMappingNode();
        }

        try
        {
            var merged = new YamlMappingNode();
            foreach (var child in node.Children)
            {
                if (IsMergeKey(child.Key))
                {
                    MergeInherited(merged, child.Value, visiting);
                }
            }

            foreach (var child in node.Children)
            {
                if (!IsMergeKey(child.Key))
                {
                    merged.Children[child.Key] = child.Value;
                }
            }

            return merged;
        }
        finally
        {
            visiting.Remove(node);
        }
    }

    private static void MergeInherited(YamlMappingNode target, YamlNode source, HashSet<YamlNode> visiting)
    {
        if (source is YamlMappingNode mapping)
        {
            foreach (var child in ResolveMergedMapping(mapping, visiting).Children)
            {
                if (!target.Children.ContainsKey(child.Key))
                {
                    target.Children[child.Key] = child.Value;
                }
            }

            return;
        }

        if (source is not YamlSequenceNode sequence)
        {
            return;
        }

        foreach (var item in sequence.Children.OfType<YamlMappingNode>())
        {
            MergeInherited(target, item, visiting);
        }
    }

    private static bool IsMergeKey(YamlNode key)
    {
        return key is YamlScalarNode { Value: "<<" };
    }

    private static SubscriptionProvider ParseProvider(string name, string providerType, YamlMappingNode node)
    {
        var vehicleType = NormalizeVehicleType(Scalar(node, "type"));
        return new SubscriptionProvider(
            name,
            providerType,
            vehicleType,
            Scalar(node, "path"),
            Count(node, providerType),
            null,
            SourceFingerprint: Fingerprint(node.ToString()));
    }

    public static string Fingerprint(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static int Count(YamlMappingNode node, string providerType)
    {
        if (providerType == "rule" && int.TryParse(Scalar(node, "ruleCount"), out var ruleCount))
        {
            return ruleCount;
        }

        if (node.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode) && proxiesNode is YamlSequenceNode proxies)
        {
            return proxies.Children.Count;
        }

        return 0;
    }

    private static string Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? value.ToString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeVehicleType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "http" => "HTTP",
            "file" => "File",
            _ => type
        };
    }
}
