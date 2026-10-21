using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Andro.Application.Abstractions;
using Andro.Domain.Sdk;

namespace Andro.Infrastructure.Downloads;

public sealed class HttpDownloader(HttpClient http) : IDownloader
{
    public async Task DownloadAsync(SdkArchive archive, string destinationFile, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var part = destinationFile + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationFile))!);

        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, archive.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // A server that ignores Range sends the whole file again: start over.
        var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var received = resumed ? existing : 0;

        await using (var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var body = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                progress?.Report(new DownloadProgress(received, archive.Size));
            }
        }

        if (!await ChecksumMatchesAsync(part, archive, ct))
        {
            File.Delete(part);
            throw new ChecksumMismatchException($"Le fichier téléchargé {archive.Url} est corrompu (somme de contrôle {archive.ChecksumType} différente).");
        }

        File.Move(part, destinationFile, overwrite: true);
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
