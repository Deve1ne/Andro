using Andro.Domain.Sdk;

namespace Andro.Application.Abstractions;

public sealed record DownloadProgress(long Received, long Total);

public sealed class ChecksumMismatchException(string message) : Exception(message);

public interface IDownloader
{
    /// <summary>
    /// Downloads <paramref name="archive"/> to <paramref name="destinationFile"/>, resuming a previous
    /// "<c>.part</c>" file, and verifies its checksum before the file appears under its final name.
    /// </summary>
    Task DownloadAsync(SdkArchive archive, string destinationFile, IProgress<DownloadProgress>? progress, CancellationToken ct);
}
