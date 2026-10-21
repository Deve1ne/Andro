using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Andro.Application.Abstractions;
using Andro.Domain.Sdk;

namespace Andro.Infrastructure.Downloads;

public sealed class DownloadStalledException(string message) : IOException(message);

/// <param name="readTimeout">How long a read may wait for bytes before the connection counts as dropped (default 60 s).</param>
public sealed class HttpDownloader(HttpClient http, TimeSpan? readTimeout = null) : IDownloader
{
    private readonly TimeSpan _readTimeout = readTimeout ?? TimeSpan.FromSeconds(60);

    public async Task DownloadAsync(SdkArchive archive, string destinationFile, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        // An archive downloaded earlier but not yet extracted (app closed during extraction) is reused.
        if (File.Exists(destinationFile) && await ChecksumMatchesAsync(destinationFile, archive, ct))
        {
            progress?.Report(new DownloadProgress(archive.Size, archive.Size));
            return;
        }

        var part = destinationFile + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationFile))!);

        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > archive.Size)
        {
            File.Delete(part);
            existing = 0;
        }

        // A complete .part (app closed between the last byte and the rename) only needs verifying.
        if (existing < archive.Size)
        {
            existing = await FetchAsync(archive, part, existing, progress, ct);
        }

        if (!await ChecksumMatchesAsync(part, archive, ct))
        {
            File.Delete(part);
            throw new ChecksumMismatchException($"Le fichier téléchargé {archive.Url} est corrompu (somme de contrôle {archive.ChecksumType} différente).");
        }

        File.Move(part, destinationFile, overwrite: true);
    }

    private async Task<long> FetchAsync(SdkArchive archive, string part, long existing, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, archive.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file no longer matches what the server has: start over.
            File.Delete(part);
            return await FetchAsync(archive, part, 0, progress, ct);
        }
        response.EnsureSuccessStatusCode();

        // A server that ignores Range sends the whole file again: start over.
        var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var received = resumed ? existing : 0;

        await using var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        while (true)
        {
            int read;
            using (var stall = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                stall.CancelAfter(_readTimeout);
                try
                {
                    read = await body.ReadAsync(buffer, stall.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new DownloadStalledException(
                        $"La connexion s'est interrompue pendant le téléchargement de {archive.Url.Segments[^1]}. Relancez : le téléchargement reprendra où il s'est arrêté.");
                }
            }
            if (read == 0) break;

            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress?.Report(new DownloadProgress(received, archive.Size));
        }
        return received;
    }

    private static async Task<bool> ChecksumMatchesAsync(string file, SdkArchive archive, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var hash = archive.ChecksumType == "sha256"
            ? await SHA256.HashDataAsync(stream, ct)
            : await SHA1.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(archive.Checksum, StringComparison.OrdinalIgnoreCase);
    }
}
