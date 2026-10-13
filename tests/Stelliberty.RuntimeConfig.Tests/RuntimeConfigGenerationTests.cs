using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace Stelliberty.RuntimeConfig.Tests;

public sealed class RuntimeConfigGenerationTests
{
    [Fact(DisplayName = "From settings normalizes ports and outbound mode")]
    public void FromSettingsNormalizesPortsAndOutboundMode()
    {
        var settings = new AppSettings
        {
            MixedPort = 70000,
            SocksPort = -1,
            HttpPort = 65535,
            OutboundMode = "global",
            IsExternalControllerEnabled = true,
            ExternalControllerAddress = " 127.0.0.1:9090 ",
            ExternalControllerSecret = "<external-controller-secret>",
            TunStack = "System",
            TunDevice = ""
        };

        var parameters = RuntimeConfigParams.FromSettings(settings);

        Assert.Equal(AppSettings.DefaultMixedPort, parameters.MixedPort);
        Assert.Equal(0, parameters.SocksPort);
        Assert.Equal(65535, parameters.HttpPort);
        Assert.Equal("Global", parameters.OutboundMode);
        Assert.Equal("127.0.0.1:9090", parameters.ExternalController);
        Assert.Equal("<external-controller-secret>", parameters.ExternalControllerSecret);
        Assert.Equal("system", parameters.TunStack);
        Assert.Equal(RuntimeConfigParams.Default.TunDevice, parameters.TunDevice);
    }


    [Fact(DisplayName = "From settings uses complete TUN defaults")]
    public void FromSettingsUsesCompleteTunDefaults()
    {
        var parameters = RuntimeConfigParams.FromSettings(new AppSettings());

        Assert.Equal(OperatingSystem.IsMacOS() ? string.Empty : "clash", parameters.TunDevice);
        Assert.True(parameters.IsTunAutoRouteEnabled);
        Assert.True(parameters.IsTunAutoRedirectEnabled);
        Assert.True(parameters.IsTunAutoDetectInterfaceEnabled);
        Assert.True(parameters.IsTunStrictRouteEnabled);
        Assert.Equal(["any:53"], parameters.TunDnsHijacks);
        Assert.Equal(9000, parameters.TunMtu);
    }

    [Theory(DisplayName = "Legacy TUN device names use automatic allocation only on macOS")]
    [InlineData("clash")]
    [InlineData(" clash ")]
    [InlineData("")]
    [InlineData(" ")]
    public void LegacyTunDeviceUsesPlatformDefault(string device)
    {
        var parameters = RuntimeConfigParams.FromSettings(new AppSettings { TunDevice = device });
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            "proxies: []\nproxy-groups: []\nrules: []",
            [],
            parameters));

        Assert.Equal(
            OperatingSystem.IsMacOS() ? string.Empty : "clash",
            Scalar(Mapping(Load(result.RuntimeConfigContent), "tun"), "device"));
    }

    [Theory(DisplayName = "Custom TUN device names are preserved")]
    [InlineData("utun7", "utun7")]
    [InlineData(" utun42 ", "utun42")]
    [InlineData("Meta", "Meta")]
    [InlineData("CLASH", "CLASH")]
    public void CustomTunDeviceIsPreserved(string device, string expected)
    {
        var parameters = RuntimeConfigParams.FromSettings(new AppSettings { TunDevice = device });

        Assert.Equal(expected, parameters.TunDevice);
    }

    [Fact(DisplayName = "From settings drops blank controller and incomplete LAN authentication")]
    public void FromSettingsDropsBlankControllerAndIncompleteLanAuthentication()
    {
        var partialSettings = new AppSettings
        {
            IsExternalControllerEnabled = true,
            ExternalControllerAddress = " ",
            ExternalControllerSecret = " <external-controller-secret> ",
            LanAuthenticationUserName = " user ",
            LanAuthenticationPassword = " "
        };
        var completeSettings = new AppSettings
        {
            LanAuthenticationUserName = " user ",
            LanAuthenticationPassword = " <proxy-password> "
        };

        var partialParameters = RuntimeConfigParams.FromSettings(partialSettings);
        var completeParameters = RuntimeConfigParams.FromSettings(completeSettings);

        Assert.Null(partialParameters.ExternalController);
        Assert.Null(partialParameters.ExternalControllerSecret);
        Assert.Empty(partialParameters.LanAuthentication);
        Assert.Equal(["user:<proxy-password>"], completeParameters.LanAuthentication);
    }

    [Fact(DisplayName = "From settings trims manual edited runtime fields before YAML generation")]
    public void FromSettingsTrimsManualEditedRuntimeFieldsBeforeYamlGeneration()
    {
        var parameters = RuntimeConfigParams.FromSettings(new AppSettings
        {
            IsExternalControllerEnabled = true,
            ExternalControllerAddress = " 127.0.0.1:9090 ",
            ExternalControllerSecret = " <external-controller-secret> ",
            IsTunEnabled = true,
            TunDevice = " Meta ",
            TunDnsHijack = [" any:53 ", " "],
            TunRouteExcludeAddresses = [" 10.0.0.0/8 ", ""],
            IsDnsOverrideEnabled = true,
            DnsListen = " :53 ",
            DnsEnhancedMode = " fake-ip ",
            FakeIpRange = " 198.18.0.1/16 ",
            FakeIpFilterMode = " blacklist ",
            NameServers = [" https://dns.example/query ", " "],
            ProxyServerNameServers = [" tls://proxy.example "],
            NameServerPolicy = [" geosite:cn = 223.5.5.5 ; 119.29.29.29 "],
            Hosts = [" example.com = 1.1.1.1 "],
            FallbackFilterGeoIpCode = " CN ",
            FallbackFilterIpCidrs = [" 240.0.0.0/4 "],
            LanAllowedIps = [" 192.168.1.0/24 "],
            LanDisallowedIps = [" 10.0.0.0/8 "],
            SkipAuthPrefixes = [" 127.0.0.1/8 "]
        });

        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            "proxies: []\nproxy-groups: []\nrules: []",
            [],
            parameters));

        var root = Load(result.RuntimeConfigContent);
        var tun = Mapping(root, "tun");
        var dns = Mapping(root, "dns");
        var policy = Mapping(dns, "nameserver-policy");
        var fallbackFilter = Mapping(dns, "fallback-filter");
        var hosts = Mapping(root, "hosts");

        Assert.Equal("<external-controller-secret>", Scalar(root, "secret"));
        Assert.Equal("Meta", Scalar(tun, "device"));
        Assert.Equal(["any:53"], SequenceValues(tun, "dns-hijack"));
        Assert.Equal(["10.0.0.0/8"], SequenceValues(tun, "route-exclude-address"));
        Assert.Equal(["192.168.1.0/24"], SequenceValues(root, "lan-allowed-ips"));
        Assert.Equal(["10.0.0.0/8"], SequenceValues(root, "lan-disallowed-ips"));
        Assert.Equal(["127.0.0.1/8"], SequenceValues(root, "skip-auth-prefixes"));
        Assert.Equal(":53", Scalar(dns, "listen"));
        Assert.Equal("fake-ip", Scalar(dns, "enhanced-mode"));
        Assert.Equal("198.18.0.1/16", Scalar(dns, "fake-ip-range"));
        Assert.Equal("blacklist", Scalar(dns, "fake-ip-filter-mode"));
        Assert.Equal(["https://dns.example/query"], SequenceValues(dns, "nameserver"));
        Assert.Equal(["tls://proxy.example"], SequenceValues(dns, "proxy-server-nameserver"));
        Assert.Equal(["223.5.5.5", "119.29.29.29"], SequenceValues(policy, "geosite:cn"));
        Assert.Equal("CN", Scalar(fallbackFilter, "geoip-code"));
        Assert.Equal(["240.0.0.0/4"], SequenceValues(fallbackFilter, "ipcidr"));
        Assert.Equal("1.1.1.1", Scalar(hosts, "example.com"));
    }

    [Fact(DisplayName = "Generator injects runtime ports mode and removes IPC endpoints")]
    public void GeneratorInjectsRuntimePortsModeAndRemovesIpcEndpoints()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            mixed-port: 1
            port: 2
            socks-port: 3
            external-controller-pipe: old-pipe
            external-controller-unix: old-unix
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default with
            {
                MixedPort = 7890,
                HttpPort = 0,
                SocksPort = 1080,
                OutboundMode = "Direct"
            }));

        var root = Load(result.RuntimeConfigContent);

        Assert.Equal("7890", Scalar(root, "mixed-port"));
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("port")));
        Assert.Equal("1080", Scalar(root, "socks-port"));
        Assert.Equal("direct", Scalar(root, "mode"));
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("external-controller-pipe")));
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("external-controller-unix")));
    }

    [Fact(DisplayName = "Generator removes external controller when disabled")]
    public void GeneratorRemovesExternalControllerWhenDisabled()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            external-controller: 127.0.0.1:9090
            secret: <external-controller-secret-old>
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default));

        var root = Load(result.RuntimeConfigContent);

        Assert.False(root.Children.ContainsKey(new YamlScalarNode("external-controller")));
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("secret")));
    }

    [Fact(DisplayName = "Generator injects external controller when enabled")]
    public void GeneratorInjectsExternalControllerWhenEnabled()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            "proxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default with
            {
                ExternalController = "127.0.0.1:9090",
                ExternalControllerSecret = "<external-controller-secret>"
            }));

        var root = Load(result.RuntimeConfigContent);

        Assert.Equal("127.0.0.1:9090", Scalar(root, "external-controller"));
        Assert.Equal("<external-controller-secret>", Scalar(root, "secret"));
    }

    [Fact(DisplayName = "Generator keeps allow LAN and bind address consistent")]
    public void GeneratorKeepsAllowLanAndBindAddressConsistent()
    {
        var generator = new RuntimeConfigGenerator();
        var disabled = generator.Generate(new RuntimeConfigGenerationRequest(
            "bind-address: 0.0.0.0\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default with { IsAllowLanEnabled = false }));
        var enabled = generator.Generate(new RuntimeConfigGenerationRequest(
            "bind-address: 127.0.0.1\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default with { IsAllowLanEnabled = true }));

        var disabledRoot = Load(disabled.RuntimeConfigContent);
        var enabledRoot = Load(enabled.RuntimeConfigContent);

        Assert.Equal("false", Scalar(disabledRoot, "allow-lan"));
        Assert.Equal("127.0.0.1", Scalar(disabledRoot, "bind-address"));
        Assert.Equal("true", Scalar(enabledRoot, "allow-lan"));
        Assert.Equal("0.0.0.0", Scalar(enabledRoot, "bind-address"));
    }

    [Fact(DisplayName = "Invalid post transform output throws with stage label")]
    public void InvalidPostTransformOutputThrowsWithStageLabel()
    {
        var request = new RuntimeConfigGenerationRequest(
            "proxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default);

        var postTransformException = Assert.Throws<InvalidOperationException>(() => new RuntimeConfigGenerator().Generate(request with
        {
            PostOverrideTransform = _ => "proxy-groups: ["
        }));

        Assert.Contains("Chain proxy output config", postTransformException.Message, StringComparison.Ordinal);
        Assert.Contains("not valid YAML", postTransformException.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Post-override transform runs before runtime injection")]
    public void PostOverrideTransformRunsBeforeRuntimeInjection()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            "mode: rule\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default with { OutboundMode = "Global" },
            content => content.Replace("mode: rule", "mode: direct\nexternal-controller-pipe: stale", StringComparison.Ordinal)));

        var root = Load(result.RuntimeConfigContent);

        Assert.Equal("global", Scalar(root, "mode"));
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("external-controller-pipe")));
    }

    [Fact(DisplayName = "Generate empty injects TUN and default DNS")]
    public void GenerateEmptyInjectsTunAndDefaultDns()
    {
        var result = new RuntimeConfigGenerator().GenerateEmpty(RuntimeConfigParams.Default with
        {
            IsTunEnabled = true,
            DnsListen = "127.0.0.1:1053"
        });

        var root = Load(result.RuntimeConfigContent);
        var tun = Mapping(root, "tun");
        var dns = Mapping(root, "dns");

        Assert.Equal("true", Scalar(tun, "enable"));
        Assert.Equal("clash", Scalar(tun, "device"));
        Assert.Equal("true", Scalar(tun, "auto-route"));
        Assert.Equal("true", Scalar(tun, "auto-redirect"));
        Assert.Equal("true", Scalar(tun, "auto-detect-interface"));
        Assert.Equal("true", Scalar(tun, "strict-route"));
        Assert.Equal("9000", Scalar(tun, "mtu"));
        Assert.Equal(["any:53"], SequenceValues(tun, "dns-hijack"));
        Assert.Equal("127.0.0.1:1053", Scalar(dns, "listen"));
        Assert.Equal("fake-ip", Scalar(dns, "enhanced-mode"));
        Assert.True(dns.Children.ContainsKey(new YamlScalarNode("nameserver")));
    }

    [Fact(DisplayName = "TUN default DNS does not override custom enhanced mode")]
    public void TunDefaultDnsDoesNotOverrideCustomEnhancedMode()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            dns:
              enhanced-mode: redir-host
              nameserver:
                - 1.1.1.1
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default with { IsTunEnabled = true }));

        var dns = Mapping(Load(result.RuntimeConfigContent), "dns");

        Assert.Equal("redir-host", Scalar(dns, "enhanced-mode"));
        Assert.Equal("1.1.1.1", ((YamlSequenceNode)dns.Children[new YamlScalarNode("nameserver")]).Children[0].ToString());
        Assert.False(dns.Children.ContainsKey(new YamlScalarNode("fake-ip-filter")));
    }

    [Fact(DisplayName = "Disabled TUN does not inject default DNS")]
    public void DisabledTunDoesNotInjectDefaultDns()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            dns:
              nameserver:
                - 1.1.1.1
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default with { IsTunEnabled = false }));

        var root = Load(result.RuntimeConfigContent);
        var tun = Mapping(root, "tun");
        var dns = Mapping(root, "dns");

        Assert.Equal("false", Scalar(tun, "enable"));
        Assert.Equal("1.1.1.1", SequenceValues(dns, "nameserver").Single());
        Assert.False(dns.Children.ContainsKey(new YamlScalarNode("enhanced-mode")));
        Assert.False(dns.Children.ContainsKey(new YamlScalarNode("fake-ip-filter")));
    }

    [Fact(DisplayName = "Empty DNS override keeps subscription DNS without TUN defaults")]
    public void EmptyDnsOverrideKeepsSubscriptionDnsWithoutTunDefaults()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            dns:
              enhanced-mode: redir-host
              nameserver:
                - 1.1.1.1
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default with
            {
                IsTunEnabled = true,
                IsDnsOverrideEnabled = true,
                DnsOverrideContent = " "
            }));

        var dns = Mapping(Load(result.RuntimeConfigContent), "dns");

        Assert.Equal("redir-host", Scalar(dns, "enhanced-mode"));
        Assert.Equal("1.1.1.1", SequenceValues(dns, "nameserver").Single());
        Assert.False(dns.Children.ContainsKey(new YamlScalarNode("fake-ip-filter")));
    }

    [Fact(DisplayName = "DNS override replaces DNS and hosts instead of TUN defaults")]
    public void DnsOverrideReplacesDnsAndHostsInsteadOfTunDefaults()
    {
        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            """
            dns:
              enhanced-mode: fake-ip
              nameserver: [1.1.1.1]
            hosts:
              old.example: 1.1.1.1
            proxies: []
            proxy-groups: []
            rules: []
            """,
            [],
            RuntimeConfigParams.Default with
            {
                IsTunEnabled = true,
                IsDnsOverrideEnabled = true,
                DnsOverrideContent = """
                dns:
                  enable: false
                  enhanced-mode: normal
                  nameserver:
                    - 9.9.9.9
                hosts:
                  new.example: 2.2.2.2
                """
            }));

        var root = Load(result.RuntimeConfigContent);
        var dns = Mapping(root, "dns");
        var hosts = Mapping(root, "hosts");

        Assert.Equal("false", Scalar(dns, "enable"));
        Assert.Equal("normal", Scalar(dns, "enhanced-mode"));
        Assert.Equal("9.9.9.9", ((YamlSequenceNode)dns.Children[new YamlScalarNode("nameserver")]).Children[0].ToString());
        Assert.False(dns.Children.ContainsKey(new YamlScalarNode("fake-ip-filter")));
        Assert.False(hosts.Children.ContainsKey(new YamlScalarNode("old.example")));
        Assert.Equal("2.2.2.2", Scalar(hosts, "new.example"));
    }

    [Fact(DisplayName = "DNS override from settings builds policy hosts and default proxy DNS")]
    public void DnsOverrideFromSettingsBuildsPolicyHostsAndDefaultProxyDns()
    {
        var parameters = RuntimeConfigParams.FromSettings(new AppSettings
        {
            IsDnsOverrideEnabled = true,
            IsDnsRespectRulesEnabled = true,
            NameServers = ["https://dns.example/query", ""],
            ProxyServerNameServers = [],
            NameServerPolicy = ["geosite:cn=system;223.5.5.5", "bad", " = empty", "geosite:private=system"],
            Hosts = ["example.com=1.1.1.1", "bad", "localhost=127.0.0.1"],
            FallbackNameServers = ["tls://fallback.example", ""],
            DirectNameServers = ["https://direct.example/query"],
            FallbackFilterIpCidrs = ["240.0.0.0/4", ""],
            FallbackFilterDomains = ["+.google.com"]
        });

        var result = new RuntimeConfigGenerator().Generate(new RuntimeConfigGenerationRequest(
            "dns:\n  nameserver: [old]\nhosts:\n  old.example: 1.1.1.1\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            parameters));

        var root = Load(result.RuntimeConfigContent);
        var dns = Mapping(root, "dns");
        var policy = Mapping(dns, "nameserver-policy");
        var fallbackFilter = Mapping(dns, "fallback-filter");
        var hosts = Mapping(root, "hosts");

        Assert.Equal(["https://dns.example/query"], SequenceValues(dns, "nameserver"));
        Assert.Equal(DnsDefaults.ProxyServerNameServers, SequenceValues(dns, "proxy-server-nameserver"));
        Assert.Equal(["tls://fallback.example"], SequenceValues(dns, "fallback"));
        Assert.Equal(["https://direct.example/query"], SequenceValues(dns, "direct-nameserver"));
        Assert.Equal(["system", "223.5.5.5"], SequenceValues(policy, "geosite:cn"));
        Assert.Equal("system", Scalar(policy, "geosite:private"));
        Assert.False(policy.Children.ContainsKey(new YamlScalarNode("bad")));
        Assert.Equal("1.1.1.1", Scalar(hosts, "example.com"));
        Assert.Equal("127.0.0.1", Scalar(hosts, "localhost"));
        Assert.False(hosts.Children.ContainsKey(new YamlScalarNode("old.example")));
        Assert.Equal(["240.0.0.0/4"], SequenceValues(fallbackFilter, "ipcidr"));
        Assert.Equal(["+.google.com"], SequenceValues(fallbackFilter, "domain"));
    }

    [Fact(DisplayName = "LAN authentication and keep-alive write or remove runtime keys")]
    public void LanAuthenticationAndKeepAliveWriteOrRemoveRuntimeKeys()
    {
        var generator = new RuntimeConfigGenerator();
        var enabled = generator.Generate(new RuntimeConfigGenerationRequest(
            "keep-alive-interval: 10\nauthentication: [old]\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default with
            {
                IsKeepAliveEnabled = true,
                KeepAliveInterval = 30,
                LanAuthentication = ["user:<proxy-password>"],
                LanAllowedIps = ["192.168.1.0/24", ""],
                LanDisallowedIps = ["10.0.0.0/8"],
                SkipAuthPrefixes = ["127.0.0.1/8"]
            }));

        var enabledRoot = Load(enabled.RuntimeConfigContent);
        Assert.Equal("30", Scalar(enabledRoot, "keep-alive-interval"));
        Assert.Equal(["user:<proxy-password>"], SequenceValues(enabledRoot, "authentication"));
        Assert.Equal(["192.168.1.0/24"], SequenceValues(enabledRoot, "lan-allowed-ips"));
        Assert.Equal(["10.0.0.0/8"], SequenceValues(enabledRoot, "lan-disallowed-ips"));
        Assert.Equal(["127.0.0.1/8"], SequenceValues(enabledRoot, "skip-auth-prefixes"));

        var disabled = generator.Generate(new RuntimeConfigGenerationRequest(
            "keep-alive-interval: 10\nauthentication: [old]\nlan-allowed-ips: [old]\nlan-disallowed-ips: [old]\nskip-auth-prefixes: [old]\nproxies: []\nproxy-groups: []\nrules: []",
            [],
            RuntimeConfigParams.Default));

        var disabledRoot = Load(disabled.RuntimeConfigContent);
        Assert.False(disabledRoot.Children.ContainsKey(new YamlScalarNode("keep-alive-interval")));
        Assert.False(disabledRoot.Children.ContainsKey(new YamlScalarNode("authentication")));
        Assert.False(disabledRoot.Children.ContainsKey(new YamlScalarNode("lan-allowed-ips")));
        Assert.False(disabledRoot.Children.ContainsKey(new YamlScalarNode("lan-disallowed-ips")));
        Assert.False(disabledRoot.Children.ContainsKey(new YamlScalarNode("skip-auth-prefixes")));
    }

    [Fact(DisplayName = "Invalid runtime ports throw before producing YAML")]
    public void InvalidRuntimePortsThrowBeforeProducingYaml()
    {
        var generator = new RuntimeConfigGenerator();

        Assert.Throws<InvalidOperationException>(() => generator.GenerateEmpty(RuntimeConfigParams.Default with { MixedPort = 0 }));
        Assert.Throws<InvalidOperationException>(() => generator.GenerateEmpty(RuntimeConfigParams.Default with { HttpPort = -1 }));
        Assert.Throws<InvalidOperationException>(() => generator.GenerateEmpty(RuntimeConfigParams.Default with { SocksPort = 70000 }));
    }

    private static YamlMappingNode Load(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Mapping(YamlMappingNode root, string key)
    {
        return (YamlMappingNode)root.Children[new YamlScalarNode(key)];
    }

    private static string Scalar(YamlMappingNode root, string key)
    {
        return root.Children[new YamlScalarNode(key)].ToString();
    }

    private static IReadOnlyList<string> SequenceValues(YamlMappingNode root, string key)
    {
        return ((YamlSequenceNode)root.Children[new YamlScalarNode(key)]).Children.Select(item => item.ToString()).ToList();
    }

}
