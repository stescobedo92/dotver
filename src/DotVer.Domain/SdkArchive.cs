namespace DotVer.Domain;

/// <summary>An SDK archive published in the official release metadata for one exact version and RID.</summary>
/// <param name="Version">Exact SDK version (e.g. 8.0.427).</param>
/// <param name="Rid">Runtime identifier the archive is built for (e.g. linux-x64).</param>
/// <param name="FileName">Local file name, taken from the download URL (e.g. dotnet-sdk-8.0.427-linux-x64.tar.gz).</param>
/// <param name="Url">Absolute https download URL from the metadata.</param>
/// <param name="Sha512">Published SHA-512 of the archive, lowercase hex; null when the metadata carries no usable hash.</param>
public sealed record SdkArchive(string Version, string Rid, string FileName, string Url, string? Sha512);

/// <summary>Progress of one archive download.</summary>
public sealed record DownloadProgress(string FileName, long BytesReceived, long? TotalBytes);

/// <summary>What happened to one archive on disk.</summary>
/// <param name="Archive">The published archive this file is a verified copy of.</param>
/// <param name="Path">Absolute path of the verified archive.</param>
/// <param name="Bytes">Size on disk.</param>
/// <param name="SkippedExisting">True when a verified copy was already there and nothing was downloaded.</param>
public sealed record SdkArchiveDownload(SdkArchive Archive, string Path, long Bytes, bool SkippedExisting);

/// <summary>
/// One (token, RID) pair of a <c>dotver download</c> run. A failed entry keeps
/// its error and never stops the other entries.
/// </summary>
public sealed record DownloadBatchEntry(
    string RequestedToken,
    string Rid,
    string? ResolvedVersion,
    SdkArchive? Archive,
    SdkArchiveDownload? Download,
    string? Error)
{
    public bool Planned => Archive is not null && Error is null;

    public bool Downloaded => Download is not null && Error is null;
}

public sealed record DownloadBatchResult(string OutputDirectory, IReadOnlyList<DownloadBatchEntry> Entries)
{
    public bool AllSucceeded => Entries.All(static entry => entry.Error is null);

    public int Failed => Entries.Count(static entry => entry.Error is not null);
}
