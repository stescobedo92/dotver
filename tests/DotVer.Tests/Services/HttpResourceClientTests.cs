using System.Net;
using System.Text;
using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class HttpResourceClientTests
{
    private const string InstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1";
    private const string CdnUrl = "https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1";

    [Fact]
    public async Task GetString_FollowsRedirectsBetweenAllowedHosts()
    {
        var handler = new StubHandler(request => request.RequestUri!.Host == "dot.net"
            ? Redirect(CdnUrl)
            : Ok("script body"));
        var client = CreateClient(handler);

        var payload = await client.GetStringAsync(InstallScriptUrl, CancellationToken.None);

        Assert.Equal("script body", payload);
        Assert.Equal([new Uri(InstallScriptUrl), new Uri(CdnUrl)], handler.Requests);
    }

    [Fact]
    public async Task GetString_RefusesRedirectToUntrustedHost()
    {
        var handler = new StubHandler(_ => Redirect("https://evil.example/dotnet-install.ps1"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStringAsync(InstallScriptUrl, CancellationToken.None));

        // The untrusted location is never contacted.
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://dot.net/v1/dotnet-install.ps1")]
    [InlineData("https://evil.example/releases-index.json")]
    [InlineData("https://user:pw@dot.net/v1/dotnet-install.ps1")]
    [InlineData("https://dot.net:8443/v1/dotnet-install.ps1")]
    public async Task GetString_RefusesUntrustedInitialUrlWithoutSendingAnything(string url)
    {
        var handler = new StubHandler(_ => Ok("never"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStringAsync(url, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetString_StopsAfterTooManyRedirects()
    {
        var handler = new StubHandler(_ => Redirect(InstallScriptUrl));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStringAsync(InstallScriptUrl, CancellationToken.None));

        Assert.InRange(handler.Requests.Count, 1, 6);
    }

    [Fact]
    public async Task GetString_RefusesOversizedPayload()
    {
        var handler = new StubHandler(_ => Ok(new string('x', 2048)));
        var client = CreateClient(handler, maxResponseBytes: 1024);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStringAsync(InstallScriptUrl, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadFile_WritesTheResponseBody()
    {
        using var sandbox = new TestSandbox();
        var handler = new StubHandler(_ => Ok("echo install"));
        var client = CreateClient(handler);
        var destination = Path.Combine(sandbox.Root, "scripts", "dotnet-install.sh");

        await client.DownloadFileAsync("https://dot.net/v1/dotnet-install.sh", destination, CancellationToken.None);

        Assert.Equal("echo install", File.ReadAllText(destination));
    }

    private static HttpResourceClient CreateClient(StubHandler handler, long? maxResponseBytes = null) =>
        maxResponseBytes is null
            ? new HttpResourceClient(new HttpClient(handler))
            : new HttpResourceClient(new HttpClient(handler), maxResponseBytes.Value);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
