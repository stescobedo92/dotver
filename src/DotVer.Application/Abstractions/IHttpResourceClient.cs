namespace DotVer.Application;

public interface IHttpResourceClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
    Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken);
}
