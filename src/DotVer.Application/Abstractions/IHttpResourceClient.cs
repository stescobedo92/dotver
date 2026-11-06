namespace DotVer.Application;

public interface IHttpResourceClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);

    Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Streams a large payload (an SDK archive) straight to disk with its own
    /// size cap instead of the in-memory cap of the other methods. Reports
    /// (bytes received, declared total or null) as data arrives and returns
    /// the number of bytes written.
    /// </summary>
    Task<long> DownloadToFileAsync(string url, string destinationPath, long maxBytes, Action<long, long?>? onProgress, CancellationToken cancellationToken);
}
