using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotVer.Application;

namespace DotVer.Infrastructure;

/// <summary>
/// Fetches the official install scripts and the releases index. The transport
/// is deliberately narrow: https only, a fixed allow-list of Microsoft hosts
/// checked on the request and on every redirect hop, a redirect cap and a
/// streaming size cap, so a poisoned redirect can never hand dotver a script
/// from somewhere else.
/// </summary>
public sealed class HttpResourceClient : IHttpResourceClient
{
    public const long DefaultMaxResponseBytes = 16 * 1024 * 1024;

    private const int MaxRedirects = 5;

    // dot.net answers with a redirect to the builds CDN; the Visual Studio
    // download host serves some archives referenced by release metadata.
    private static readonly string[] AllowedHosts =
    [
        "dot.net",
        "builds.dotnet.microsoft.com",
        "download.visualstudio.microsoft.com"
    ];

    private readonly HttpClient _httpClient;
    private readonly long _maxResponseBytes;
    private readonly IDownloadSources? _sources;
    private readonly IReadOnlyCollection<string> _baseAllowedHosts;

    public HttpResourceClient(
        HttpClient httpClient,
        long maxResponseBytes = DefaultMaxResponseBytes,
        IDownloadSources? sources = null,
        IReadOnlyCollection<string>? allowedHosts = null)
    {
        _httpClient = httpClient;
        _maxResponseBytes = maxResponseBytes;
        _sources = sources;
        _baseAllowedHosts = allowedHosts ?? AllowedHosts;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dotver", "1.0.0"));
    }

    public Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken) => FetchAsync(url, cancellationToken);

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var bytes = await FetchAsync(url, cancellationToken).ConfigureAwait(false);
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var bytes = await FetchAsync(url, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken)
    {
        // A configured mirror widens the allow-list by exactly its own host(s).
        var allowedHosts = _sources is null
            ? _baseAllowedHosts
            : _baseAllowedHosts.Concat((await _sources.ResolveAsync(cancellationToken).ConfigureAwait(false)).ExtraAllowedHosts).ToArray();
        var uri = ValidateUri(url, "requested URL", allowedHosts);
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (hop >= MaxRedirects)
                {
                    throw new InvalidOperationException($"Too many redirects while fetching from {uri.Host}.");
                }

                var location = response.Headers.Location
                    ?? throw new InvalidOperationException($"Redirect from {uri.Host} did not carry a Location header.");
                uri = ValidateUri(new Uri(uri, location).AbsoluteUri, "redirect target", allowedHosts);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var declared && declared > _maxResponseBytes)
        {
            throw new InvalidOperationException("The response is unexpectedly large; refusing to read it.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _maxResponseBytes)
            {
                throw new InvalidOperationException("The response is unexpectedly large; refusing to read it.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri ValidateUri(string url, string role, IReadOnlyCollection<string> allowedHosts)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return uri;
        }

        throw new InvalidOperationException(
            $"Refusing to fetch the {role} '{TerminalText.Sanitize(url)}': only https on {string.Join(", ", allowedHosts)} is allowed (add a mirror with 'dotver mirror set').");
    }
}
