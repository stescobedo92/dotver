using System.Net.Http.Headers;
using DotVer.Application;

namespace DotVer.Infrastructure;

public sealed class HttpResourceClient : IHttpResourceClient
{
    private readonly HttpClient _httpClient;

    public HttpResourceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dotver", "1.0.0"));
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
        await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

    public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}
