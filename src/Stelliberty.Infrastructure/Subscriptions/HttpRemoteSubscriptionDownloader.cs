using System.Net;
using System.Net.Http.Headers;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Http;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class HttpRemoteSubscriptionDownloader : IRemoteSubscriptionDownloader
{
    private const int MaxSubscriptionContentBytes = 10 * 1024 * 1024;
    // 为订阅服务动态生成配置预留时间，响应等待和正文下载共用总时限。
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<(string Host, int Port)> _coreProxyEndpointProvider;

    public HttpRemoteSubscriptionDownloader(string coreProxyHost = "127.0.0.1", int coreProxyPort = AppSettings.DefaultMixedPort)
        : this(() => (coreProxyHost, coreProxyPort))
    {
    }

    public HttpRemoteSubscriptionDownloader(Func<(string Host, int Port)> coreProxyEndpointProvider)
    {
        _coreProxyEndpointProvider = coreProxyEndpointProvider;
    }

    public async Task<RemoteSubscriptionDownloadResult> DownloadAsync(RemoteSubscriptionDownloadRequest request, CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        if (request.ProxyMode == SubscriptionUpdateProxyMode.Direct)
        {
            handler.UseProxy = false;
        }
        else if (request.ProxyMode == SubscriptionUpdateProxyMode.SystemProxy)
        {
            handler.UseProxy = true;
            handler.Proxy = WebRequest.DefaultWebProxy;
        }
        else if (request.ProxyMode == SubscriptionUpdateProxyMode.Core)
        {
            var endpoint = _coreProxyEndpointProvider();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy($"http://{endpoint.Host}:{endpoint.Port}");
        }

        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(DownloadTimeout);
        var requestCancellationToken = timeoutCancellation.Token;
        using var message = new HttpRequestMessage(HttpMethod.Get, request.SourceLocation)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        message.Headers.UserAgent.ParseAdd(SubscriptionDefaults.NormalizeUserAgent(request.UserAgent));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/yaml"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/yaml"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-yaml"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
        message.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        message.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));

        try
        {
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestCancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new HttpRequestException(
                    BuildTransportFailureMessage(request, exception),
                    null,
                    exception.StatusCode);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateResponseFailureException(request, response);
                }

                await response.Content.LoadIntoBufferAsync(MaxSubscriptionContentBytes, requestCancellationToken);
                var content = await HttpContentTextReader.ReadAsStringAsync(response.Content, requestCancellationToken);
                if (LooksLikeHtmlDocument(content))
                {
                    throw CreateUnexpectedHtmlResponseException(request, response);
                }

                var trafficInfo = response.Headers.TryGetValues("subscription-userinfo", out var values)
                    ? SubscriptionTrafficInfo.ParseHeader(values.FirstOrDefault() ?? string.Empty)
                    : null;
                AppLogger.Info($"Remote subscription downloaded: proxy={request.ProxyMode}");
                return new RemoteSubscriptionDownloadResult(content, trafficInfo);
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Subscription download timed out after {DownloadTimeout.TotalSeconds:0} seconds: proxy={request.ProxyMode}");
        }
    }

    private static HttpRequestException CreateUnexpectedHtmlResponseException(
        RemoteSubscriptionDownloadRequest request,
        HttpResponseMessage response)
    {
        return new HttpRequestException(
            $"Subscription download returned an HTML page instead of a configuration: HTTP {(int)response.StatusCode}; proxy={request.ProxyMode}",
            null,
            response.StatusCode);
    }

    private static HttpRequestException CreateResponseFailureException(
        RemoteSubscriptionDownloadRequest request,
        HttpResponseMessage response)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        var requestedUri = Uri.TryCreate(request.SourceLocation, UriKind.Absolute, out var sourceUri) ? sourceUri : null;
        var wasRedirected = requestedUri is not null
            && finalUri is not null
            && Uri.Compare(requestedUri, finalUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0;
        var contentLength = response.Content.Headers.ContentLength?.ToString() ?? "unknown";
        var details = new List<string>
        {
            $"HTTP {(int)response.StatusCode}",
            $"redirected={wasRedirected}",
            $"proxy={request.ProxyMode}",
            $"length={contentLength}"
        };

        return new HttpRequestException(
            $"Subscription download request failed: {string.Join("; ", details)}",
            null,
            response.StatusCode);
    }

    private static bool LooksLikeHtmlDocument(string content)
    {
        var prefix = TrimHtmlPreamble(content.AsSpan());
        return prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<char> TrimHtmlPreamble(ReadOnlySpan<char> content)
    {
        while (true)
        {
            content = content.TrimStart();
            while (!content.IsEmpty && content[0] is '\uFEFF' or '\u200B')
            {
                content = content[1..].TrimStart();
            }

            if (content.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                var declarationEnd = content.IndexOf("?>", StringComparison.Ordinal);
                if (declarationEnd < 0)
                {
                    return content;
                }

                content = content[(declarationEnd + 2)..];
                continue;
            }

            if (content.StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = content.IndexOf("-->", StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    return content;
                }

                content = content[(commentEnd + 3)..];
                continue;
            }

            return content;
        }
    }

    private static string BuildTransportFailureMessage(RemoteSubscriptionDownloadRequest request, HttpRequestException exception)
    {
        return $"Subscription download request could not be completed: proxy={request.ProxyMode}; error={exception.HttpRequestError}";
    }
}
