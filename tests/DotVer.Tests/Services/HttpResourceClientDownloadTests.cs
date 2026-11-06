using System.Net;
using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class HttpResourceClientDownloadTests
{
    private const string ArchiveUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-linux-x64.tar.gz";
    private const string RedirectingUrl = "https://dot.net/download/dotnet-sdk-8.0.427-linux-x64.tar.gz";

    [Fact]
    public async Task DownloadToFile_StreamsTheBodyToDiskAndReportsProgress()
    {
        using var sandbox = new TestSandbox();
        var payload = new byte[300_000];
        new Random(7).NextBytes(payload);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var client = new HttpResourceClient(new HttpClient(handler));
        var destination = Path.Combine(sandbox.Root, "archives", "sdk.tar.gz");
        var updates = new List<(long Received, long? Total)>();

        var written = await client.DownloadToFileAsync(ArchiveUrl, destination, maxBytes: 1_000_000, (received, total) => updates.Add((received, total)), CancellationToken.None);

        Assert.Equal(payload.Length, written);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.NotEmpty(updates);
        Assert.Equal((payload.Length, (long?)payload.Length), updates[^1]);
    }

    [Fact]
    public async Task DownloadToFile_FollowsRedirectsOnlyBetweenAllowedHosts()
    {
        using var sandbox = new TestSandbox();
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "dot.net"
            ? Redirect(ArchiveUrl)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var client = new HttpResourceClient(new HttpClient(handler));
        var destination = Path.Combine(sandbox.Root, "sdk.tar.gz");

        var written = await client.DownloadToFileAsync(RedirectingUrl, destination, maxBytes: 1024, onProgress: null, CancellationToken.None);

        Assert.Equal(3, written);
        Assert.Equal([new Uri(RedirectingUrl), new Uri(ArchiveUrl)], handler.Requests);
    }

    [Fact]
    public async Task DownloadToFile_RefusesADeclaredSizeAboveTheCapBeforeReading()
    {
        using var sandbox = new TestSandbox();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) });
        var client = new HttpResourceClient(new HttpClient(handler));
        var destination = Path.Combine(sandbox.Root, "sdk.tar.gz");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadToFileAsync(ArchiveUrl, destination, maxBytes: 1024, onProgress: null, CancellationToken.None));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task DownloadToFile_StopsAnUndeclaredBodyThatGrowsPastTheCap()
    {
        using var sandbox = new TestSandbox();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[4096]) });
        var client = new HttpResourceClient(new HttpClient(handler));
        var destination = Path.Combine(sandbox.Root, "sdk.tar.gz");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadToFileAsync(ArchiveUrl, destination, maxBytes: 1024, onProgress: null, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadToFile_RefusesAnUntrustedHostWithoutSendingAnything()
    {
        using var sandbox = new TestSandbox();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        var client = new HttpResourceClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadToFileAsync("https://evil.example/sdk.tar.gz", Path.Combine(sandbox.Root, "sdk.tar.gz"), 1024, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A body with no Content-Length, like a chunked CDN response.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
