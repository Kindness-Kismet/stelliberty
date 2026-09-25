using Stelliberty.Application.Connections;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Domain.Rules;
using Xunit;

namespace Stelliberty.Monitoring.Tests;

public sealed class MonitoringBusinessRulesTests
{
    [Fact(DisplayName = "Connection parser handles core JSON value shapes with explicit fallback time")]
    public void ConnectionParserHandlesCoreJsonValueShapesWithExplicitFallbackTime()
    {
        var connections = new ConnectionParser().Parse(
            """
            {
              "connections": [
                {
                  "id": "c1",
                  "upload": "100",
                  "download": 200,
                  "chains": ["HK", "GLOBAL"],
                  "rule": "DOMAIN",
                  "rulePayload": "example.com",
                  "metadata": {
                    "network": "tcp",
                    "sourceIP": "127.0.0.1",
                    "sourcePort": 5000,
                    "destinationIP": "1.1.1.1",
                    "destinationPort": "443",
                    "destinationGeoIP": ["US"],
                    "host": "example.com",
                    "process": "browser"
                  }
                }
              ]
            }
            """,
            DateTimeOffset.UnixEpoch);

        var connection = Assert.Single(connections);
        Assert.Equal("c1", connection.Id);
        Assert.Equal(100, connection.Upload);
        Assert.Equal(200, connection.Download);
        Assert.Equal(DateTimeOffset.UnixEpoch, connection.Start);
        Assert.Equal("443", connection.Metadata.DestinationPort);
        Assert.Equal(["HK", "GLOBAL"], connection.Chains);
        Assert.Equal("HK", connection.ProxyNode);
        Assert.Equal("GLOBAL", connection.ProxyGroup);
    }

    [Fact(DisplayName = "Connection filter classifies by the actual outbound instead of the matched policy group")]
    public void ConnectionFilterClassifiesByTheActualOutboundInsteadOfTheMatchedPolicyGroup()
    {
        var filter = new ConnectionFilter();
        IReadOnlyList<ConnectionInfo> connections =
        [
            new ConnectionInfo("direct-rule", Chains: ["DIRECT"]),
            new ConnectionInfo("direct-via-group", Chains: ["DIRECT", "Final"]),
            new ConnectionInfo("proxy-via-group", Chains: ["HK", "Proxy"]),
            new ConnectionInfo("no-chain")
        ];

        var direct = filter.Apply(connections, ConnectionFilterLevel.Direct, string.Empty);
        var proxy = filter.Apply(connections, ConnectionFilterLevel.Proxy, string.Empty);

        Assert.Equal(["direct-rule", "direct-via-group", "no-chain"], direct.Select(connection => connection.Id));
        Assert.Equal(["proxy-via-group"], proxy.Select(connection => connection.Id));
    }

    [Fact(DisplayName = "Connection reducer freezes when paused and clamps sample window")]
    public void ConnectionReducerFreezesWhenPausedAndClampsSampleWindow()
    {
        var reducer = new ConnectionListReducer();
        var first = reducer.ApplyIncoming(
            ConnectionListState.Initial,
            [Connection("c1", upload: 0, download: 0)],
            DateTimeOffset.UnixEpoch);
        var second = reducer.ApplyIncoming(
            first,
            [Connection("c1", upload: 1000, download: 500)],
            DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        var paused = reducer.TogglePause(second);
        var frozen = reducer.ApplyIncoming(
            paused,
            [Connection("c1", upload: 2000, download: 1000)],
            DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(4000, second.Connections[0].UploadSpeed);
        Assert.Equal(2000, second.Connections[0].DownloadSpeed);
        Assert.Equal(second.Connections[0].Upload, frozen.Connections[0].Upload);
    }

    [Fact(DisplayName = "Core log parser handles JSON and text lines with explicit fallback time")]
    public void CoreLogParserHandlesJsonAndTextLinesWithExplicitFallbackTime()
    {
        var parser = new CoreLogParser();

        var jsonLogs = parser.Parse(
            """[{"type":"warning","payload":"slow"},{"level":"error","msg":"failed"}]""",
            DateTimeOffset.UnixEpoch);
        var textLog = parser.Parse(
            "time=\"2026-01-01T00:00:00Z\" level=debug msg=\"hello world\"",
            DateTimeOffset.UnixEpoch);

        Assert.Equal([CoreLogLevel.Warning, CoreLogLevel.Error], jsonLogs.Select(log => log.Level));
        Assert.All(jsonLogs, log => Assert.Equal(DateTimeOffset.UnixEpoch, log.Timestamp));
        Assert.Equal(CoreLogLevel.Debug, textLog.Single().Level);
        Assert.Equal("hello world", textLog.Single().Payload);
    }

    [Fact(DisplayName = "Core log reducer truncates old entries and honors pause")]
    public void CoreLogReducerTruncatesOldEntriesAndHonorsPause()
    {
        var reducer = new CoreLogReducer();
        var logs = Enumerable.Range(0, 2100)
            .Select(index => new CoreLogMessage("INFO", $"log-{index}", DateTimeOffset.UnixEpoch))
            .ToList();

        var state = reducer.Append(CoreLogState.Initial, logs);
        var paused = reducer.TogglePause(state);
        var frozen = reducer.Append(
            paused,
            [new CoreLogMessage("ERROR", "new", DateTimeOffset.UnixEpoch)]);

        Assert.Equal(2000, state.Logs.Count);
        Assert.Equal("log-100", state.Logs[0].Payload);
        Assert.Equal(state.Logs.Count, frozen.Logs.Count);
        Assert.DoesNotContain(frozen.Logs, log => log.Payload == "new");
    }

    [Fact(DisplayName = "Rule parser handles core JSON and YAML rules")]
    public void RuleParserHandlesCoreJsonAndYamlRules()
    {
        var parser = new RuleParser();
        var coreRules = parser.Parse("""{"rules":[{"type":"DOMAIN","payload":"example.com","proxy":"PROXY"}]}""");
        var yamlRules = parser.Parse(
            """
            rule-providers:
              reject:
                type: http
                path: ./reject.yaml
                ruleCount: 2
            rules:
              - DOMAIN-SUFFIX,example.com,PROXY
              - MATCH,DIRECT
            """);

        Assert.Equal("example.com", coreRules.Single().Payload);
        Assert.Contains(
            yamlRules,
            rule => rule.Type == "RULE-PROVIDER" && rule.Payload == "reject" && rule.RuleCount == 2);
        Assert.Contains(yamlRules, rule => rule.Type == "MATCH" && rule.Proxy == "DIRECT");
    }

    [Fact(DisplayName = "Rule type classifier buckets known rule types")]
    public void RuleTypeClassifierBucketsKnownRuleTypes()
    {
        Assert.Equal(RuleTypeBucket.Domain, RuleTypeClassifier.Classify("DOMAIN-SUFFIX"));
        Assert.Equal(RuleTypeBucket.Ip, RuleTypeClassifier.Classify("GEOIP"));
        Assert.Equal(RuleTypeBucket.Ip, RuleTypeClassifier.Classify("SRC-IP-CIDR"));
        Assert.Equal(RuleTypeBucket.RuleSet, RuleTypeClassifier.Classify("RULE-PROVIDER"));
        Assert.Equal(RuleTypeBucket.Other, RuleTypeClassifier.Classify("MATCH"));
    }

    private static ConnectionInfo Connection(string id, long upload, long download)
    {
        return new ConnectionInfo(id, upload, download);
    }
}
