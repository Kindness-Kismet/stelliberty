using Stelliberty.Domain.Subscriptions;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

// 缺失端口使用协议默认值：TLS 443、HTTP 80、SOCKS 1080。
public sealed partial class SubscriptionContentNormalizer
{
    private static YamlMappingNode? ParseHysteria2(string link)
    {
        var normalizedLink = link.StartsWith("hy2://", StringComparison.Ordinal)
            ? "hysteria2://" + link["hy2://".Length..]
            : link;
        if (!Uri.TryCreate(normalizedLink, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "Hysteria2" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "hysteria2");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        Set(proxy, "password", Uri.UnescapeDataString(uri.UserInfo));
        Set(proxy, "skip-cert-verify", BoolQuery(query, "insecure") ? "true" : "false");
        SetIfNotBlank(proxy, "sni", query.GetValueOrDefault("sni", string.Empty));
        SetIfNotBlank(proxy, "obfs", query.GetValueOrDefault("obfs", string.Empty));
        SetIfNotBlank(proxy, "obfs-password", query.GetValueOrDefault("obfs-password", string.Empty));
        SetIfNotBlank(proxy, "fingerprint", query.GetValueOrDefault("pinSHA256", string.Empty));
        SetIfNotBlank(proxy, "up", query.GetValueOrDefault("up", string.Empty));
        SetIfNotBlank(proxy, "down", query.GetValueOrDefault("down", string.Empty));
        SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
        return proxy;
    }

    private static YamlMappingNode? ParseHysteria(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "Hysteria" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "hysteria");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        SetIfNotBlank(proxy, "auth", Uri.UnescapeDataString(uri.UserInfo));
        SetIfNotBlank(proxy, "auth-str", query.GetValueOrDefault("auth", string.Empty));
        Set(proxy, "protocol", query.GetValueOrDefault("protocol", "udp"));
        Set(proxy, "up", query.GetValueOrDefault("up", IntQuery(query, "upmbps", 10).ToString()));
        Set(proxy, "down", query.GetValueOrDefault("down", IntQuery(query, "downmbps", 50).ToString()));
        Set(proxy, "skip-cert-verify", BoolQuery(query, "insecure") ? "true" : "false");
        SetIfNotBlank(proxy, "obfs", query.GetValueOrDefault("obfs", string.Empty));
        SetIfNotBlank(proxy, "sni", query.GetValueOrDefault("peer", string.Empty));
        SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
        return proxy;
    }

    private static YamlMappingNode? ParseTuic(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var passwordSeparatorIndex = uri.UserInfo.IndexOf(':');
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "TUIC" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "tuic");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        Set(proxy, "udp", "true");
        if (passwordSeparatorIndex >= 0)
        {
            Set(proxy, "uuid", Uri.UnescapeDataString(uri.UserInfo[..passwordSeparatorIndex]));
            Set(proxy, "password", Uri.UnescapeDataString(uri.UserInfo[(passwordSeparatorIndex + 1)..]));
        }
        else
        {
            Set(proxy, "token", Uri.UnescapeDataString(uri.UserInfo));
        }

        Set(proxy, "skip-cert-verify", BoolQuery(query, "insecure") ? "true" : "false");
        SetIfNotBlank(proxy, "sni", query.GetValueOrDefault("sni", string.Empty));
        SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
        SetBoolIfTrue(proxy, "disable-sni", BoolQuery(query, "disable_sni"));
        SetIfNotBlank(proxy, "udp-relay-mode", query.GetValueOrDefault("udp_relay_mode", string.Empty));
        SetIfNotBlank(proxy, "congestion-controller", query.GetValueOrDefault("congestion_control", query.GetValueOrDefault("congestion-controller", string.Empty)));
        return proxy;
    }

    private static YamlMappingNode? ParseHttp(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "HTTP" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "http");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : uri.Scheme == "https" ? 443 : 80).ToString());
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var passwordSeparatorIndex = uri.UserInfo.IndexOf(':');
            Set(proxy, "username", Uri.UnescapeDataString(passwordSeparatorIndex >= 0 ? uri.UserInfo[..passwordSeparatorIndex] : uri.UserInfo));
            if (passwordSeparatorIndex >= 0)
            {
                Set(proxy, "password", Uri.UnescapeDataString(uri.UserInfo[(passwordSeparatorIndex + 1)..]));
            }
        }

        if (uri.Scheme == "https")
        {
            Set(proxy, "tls", "true");
        }

        return proxy;
    }

    private static YamlMappingNode? ParseSocks(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "SOCKS5" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "socks5");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 1080).ToString());
        Set(proxy, "udp", "true");
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var passwordSeparatorIndex = uri.UserInfo.IndexOf(':');
            Set(proxy, "username", Uri.UnescapeDataString(passwordSeparatorIndex >= 0 ? uri.UserInfo[..passwordSeparatorIndex] : uri.UserInfo));
            if (passwordSeparatorIndex >= 0)
            {
                Set(proxy, "password", Uri.UnescapeDataString(uri.UserInfo[(passwordSeparatorIndex + 1)..]));
            }
        }

        return proxy;
    }

    private static YamlMappingNode? ParseShadowsocksR(string link)
    {
        var decoded = DecodeBase64Text(link["ssr://".Length..]);
        var parts = decoded.Split("/?", 2, StringSplitOptions.None);
        var mainParts = parts[0].Split(':');
        if (mainParts.Length < 6 || !int.TryParse(mainParts[1], out var port))
        {
            return null;
        }

        var query = parts.Length > 1 ? ParseQuery(parts[1]) : [];
        var proxy = new YamlMappingNode();
        Set(proxy, "name", DecodeBase64Text(query.GetValueOrDefault("remarks", string.Empty), "ShadowsocksR"));
        Set(proxy, "type", "ssr");
        Set(proxy, "server", mainParts[0]);
        Set(proxy, "port", port.ToString());
        Set(proxy, "cipher", mainParts[3]);
        Set(proxy, "password", DecodeBase64Text(mainParts[5]));
        Set(proxy, "protocol", mainParts[2]);
        Set(proxy, "obfs", mainParts[4]);
        Set(proxy, "udp", "true");
        SetIfNotBlank(proxy, "obfs-param", DecodeBase64Text(query.GetValueOrDefault("obfsparam", string.Empty)));
        SetIfNotBlank(proxy, "protocol-param", DecodeBase64Text(query.GetValueOrDefault("protoparam", string.Empty)));
        return proxy;
    }

    private static YamlMappingNode? ParseVless(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var network = V2RayNetwork(query);
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "VLESS" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "vless");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        Set(proxy, "uuid", Uri.UnescapeDataString(uri.UserInfo));
        Set(proxy, "network", network);
        Set(proxy, "udp", "true");
        SetIfNotBlank(proxy, "encryption", query.GetValueOrDefault("encryption", string.Empty));
        SetBoolIfTrue(proxy, "skip-cert-verify", BoolQuery(query, "allowInsecure"));
        if (query.GetValueOrDefault("security") == "reality")
        {
            Set(proxy, "tls", "true");
            Set(proxy, "client-fingerprint", query.GetValueOrDefault("fp", "chrome"));
            SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
            SetIfNotBlank(proxy, "servername", query.GetValueOrDefault("sni", string.Empty));
            SetIfNotBlank(proxy, "flow", query.GetValueOrDefault("flow", string.Empty));
            var realityOptions = new YamlMappingNode();
            Set(realityOptions, "public-key", query.GetValueOrDefault("pbk", string.Empty));
            Set(realityOptions, "short-id", query.GetValueOrDefault("sid", string.Empty));
            Set(proxy, "reality-opts", realityOptions);
        }
        else if (query.GetValueOrDefault("security") == "tls")
        {
            Set(proxy, "tls", "true");
            Set(proxy, "client-fingerprint", query.GetValueOrDefault("fp", "chrome"));
            SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
            SetIfNotBlank(proxy, "servername", query.GetValueOrDefault("sni", string.Empty));
        }

        SetV2RayTransportOptions(proxy, network, query);
        return proxy;
    }

    private static YamlMappingNode? ParseTrojan(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var network = query.GetValueOrDefault("type", string.Empty);
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "Trojan" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "trojan");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        Set(proxy, "password", Uri.UnescapeDataString(uri.UserInfo));
        Set(proxy, "udp", "true");
        Set(proxy, "skip-cert-verify", BoolQuery(query, "allowInsecure") ? "true" : "false");
        SetIfNotBlank(proxy, "sni", query.GetValueOrDefault("sni", string.Empty));
        Set(proxy, "client-fingerprint", query.GetValueOrDefault("fp", "chrome"));
        SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
        if (network is "ws" or "grpc")
        {
            Set(proxy, "network", network);
            SetV2RayTransportOptions(proxy, network, query);
        }

        return proxy;
    }

    private static YamlMappingNode? ParseVmess(string link)
    {
        try
        {
            using var document = JsonDocument.Parse(DecodeBase64Text(link["vmess://".Length..]));
            var root = document.RootElement;
            var proxy = new YamlMappingNode();
            var network = JsonString(root, "net", "tcp");
            Set(proxy, "name", JsonString(root, "ps", "VMess"));
            Set(proxy, "type", "vmess");
            Set(proxy, "server", JsonString(root, "add"));
            Set(proxy, "port", JsonIntString(root, "port", "443"));
            Set(proxy, "uuid", JsonString(root, "id"));
            Set(proxy, "alterId", JsonIntString(root, "aid", "0"));
            Set(proxy, "cipher", JsonString(root, "scy", "auto"));
            Set(proxy, "udp", "true");
            Set(proxy, "network", network);
            if (JsonString(root, "tls") == "tls")
            {
                Set(proxy, "tls", "true");
                SetIfNotBlank(proxy, "servername", JsonString(root, "sni"));
                SetBoolIfTrue(proxy, "skip-cert-verify", JsonString(root, "allowInsecure") == "1");
                SetIfNotBlank(proxy, "client-fingerprint", JsonString(root, "fp"));
                SetAlpn(proxy, JsonString(root, "alpn"));
            }

            if (network == "ws")
            {
                var wsOptions = new YamlMappingNode();
                Set(wsOptions, "path", JsonString(root, "path", "/"));
                var host = JsonString(root, "host");
                if (!string.IsNullOrWhiteSpace(host))
                {
                    var headers = new YamlMappingNode();
                    Set(headers, "Host", host);
                    Set(wsOptions, "headers", headers);
                }

                Set(proxy, "ws-opts", wsOptions);
            }

            if (network == "grpc")
            {
                var grpcOptions = new YamlMappingNode();
                Set(grpcOptions, "grpc-service-name", JsonString(root, "path"));
                Set(proxy, "grpc-opts", grpcOptions);
            }

            if (network == "h2")
            {
                var h2Options = new YamlMappingNode();
                Set(h2Options, "path", JsonString(root, "path", "/"));
                var host = JsonString(root, "host");
                if (!string.IsNullOrWhiteSpace(host))
                {
                    Set(h2Options, "host", Sequence(host.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                }

                Set(proxy, "h2-opts", h2Options);
            }

            return proxy;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            return ParseVmessAead(link);
        }
    }

    private static YamlMappingNode? ParseVmessAead(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var network = V2RayNetwork(query);
        var proxy = new YamlMappingNode();
        Set(proxy, "name", string.IsNullOrWhiteSpace(uri.Fragment) ? "VMess" : Uri.UnescapeDataString(uri.Fragment[1..]));
        Set(proxy, "type", "vmess");
        Set(proxy, "server", uri.DnsSafeHost);
        Set(proxy, "port", (uri.Port > 0 ? uri.Port : 443).ToString());
        Set(proxy, "uuid", Uri.UnescapeDataString(uri.UserInfo));
        Set(proxy, "alterId", "0");
        Set(proxy, "cipher", query.GetValueOrDefault("encryption", "auto"));
        Set(proxy, "udp", "true");
        Set(proxy, "network", network);
        SetBoolIfTrue(proxy, "skip-cert-verify", BoolQuery(query, "allowInsecure"));
        if (query.GetValueOrDefault("security") is "tls" or "reality")
        {
            Set(proxy, "tls", "true");
            Set(proxy, "client-fingerprint", query.GetValueOrDefault("fp", "chrome"));
            SetAlpn(proxy, query.GetValueOrDefault("alpn", string.Empty));
            SetIfNotBlank(proxy, "servername", query.GetValueOrDefault("sni", string.Empty));
            if (query.GetValueOrDefault("security") == "reality")
            {
                var realityOptions = new YamlMappingNode();
                Set(realityOptions, "public-key", query.GetValueOrDefault("pbk", string.Empty));
                Set(realityOptions, "short-id", query.GetValueOrDefault("sid", string.Empty));
                Set(proxy, "reality-opts", realityOptions);
            }
        }

        SetV2RayTransportOptions(proxy, network, query);
        return proxy;
    }

    private static YamlMappingNode? ParseShadowsocks(string link)
    {
        var body = link["ss://".Length..];
        var fragmentIndex = body.IndexOf('#');
        var name = fragmentIndex >= 0 ? Uri.UnescapeDataString(body[(fragmentIndex + 1)..]) : "Shadowsocks";
        var main = fragmentIndex >= 0 ? body[..fragmentIndex] : body;
        if (!main.Contains('@', StringComparison.Ordinal))
        {
            main = DecodeBase64Text(main, main);
        }

        var atIndex = main.IndexOf('@');
        if (atIndex <= 0 || main.Contains('?', StringComparison.Ordinal))
        {
            return null;
        }

        var authPart = DecodeBase64IfNeeded(main[..atIndex]);
        var rest = main[(atIndex + 1)..];
        var serverPort = rest;
        var authSeparatorIndex = authPart.IndexOf(':');
        var portSeparatorIndex = serverPort.LastIndexOf(':');
        // ss 链接必须包含 method:password@server:port 及全部分隔符。
        if (authSeparatorIndex <= 0 || portSeparatorIndex <= 0 || !int.TryParse(serverPort[(portSeparatorIndex + 1)..], out var port))
        {
            return null;
        }

        var proxy = new YamlMappingNode();
        Set(proxy, "name", name);
        Set(proxy, "type", "ss");
        Set(proxy, "server", serverPort[..portSeparatorIndex].Trim('[', ']'));
        Set(proxy, "port", port.ToString());
        Set(proxy, "cipher", authPart[..authSeparatorIndex]);
        Set(proxy, "password", authPart[(authSeparatorIndex + 1)..]);
        Set(proxy, "udp", "true");
        return proxy;
    }
}
