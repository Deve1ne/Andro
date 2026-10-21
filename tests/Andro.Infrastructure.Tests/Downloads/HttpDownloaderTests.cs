using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Andro.Application.Abstractions;
using Andro.Domain.Sdk;
using Andro.Infrastructure.Downloads;

namespace Andro.Infrastructure.Tests.Downloads;

public sealed class HttpDownloaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("andro-dl-").FullName;
    private readonly byte[] _payload = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Target => Path.Combine(_dir, "file.zip");

    private SdkArchive Archive(string type = "sha256", string? checksum = null) => new(
        new Uri("https://dl.google.com/android/repository/file.zip"),
        _payload.Length,
        type,
        checksum ?? Convert.ToHexString(type == "sha1" ? SHA1.HashData(_payload) : SHA256.HashData(_payload)).ToLowerInvariant(),
        null);

    [Fact]
    public async Task Downloads_and_verifies_sha256()
    {
        await new HttpDownloader(new HttpClient(new FakeServer(_payload))).DownloadAsync(Archive(), Target, null, default);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task Verifies_sha1_when_only_sha1_given()
    {
        await new HttpDownloader(new HttpClient(new FakeServer(_payload))).DownloadAsync(Archive("sha1"), Target, null, default);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target));
    }

    [Fact]
    public async Task Checksum_mismatch_deletes_file_and_throws()
    {
        var downloader = new HttpDownloader(new HttpClient(new FakeServer(_payload)));

        await Assert.ThrowsAsync<ChecksumMismatchException>(() => downloader.DownloadAsync(Archive(checksum: new string('0', 64)), Target, null, default));

        Assert.False(File.Exists(Target));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task Resumes_from_partial_file()
    {
        await File.WriteAllBytesAsync(Target + ".part", _payload[..40]);
        var server = new FakeServer(_payload);

        await new HttpDownloader(new HttpClient(server)).DownloadAsync(Archive(), Target, null, default);

        Assert.Equal(40, server.LastRange?.From);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target));
    }

    [Fact]
    public async Task Restarts_when_server_ignores_range()
    {
        await File.WriteAllBytesAsync(Target + ".part", _payload[..40]);

        await new HttpDownloader(new HttpClient(new FakeServer(_payload, honourRange: false))).DownloadAsync(Archive(), Target, null, default);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target));
    }

    [Fact]
    public async Task Reports_progress_up_to_total()
    {
        var reports = new List<DownloadProgress>();

        await new HttpDownloader(new HttpClient(new FakeServer(_payload))).DownloadAsync(Archive(), Target, new SyncProgress(reports.Add), default);

        Assert.NotEmpty(reports);
        Assert.Equal(new DownloadProgress(100, 100), reports[^1]);
    }

    [Fact]
    public async Task Cancellation_keeps_part_file()
    {
        using var cts = new CancellationTokenSource();
        var server = new FakeServer(_payload, failAfter: 30, onFail: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HttpDownloader(new HttpClient(server)).DownloadAsync(Archive(), Target, null, cts.Token));

        Assert.True(File.Exists(Target + ".part"));
        Assert.False(File.Exists(Target));
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>Serves a byte array, honouring "Range: bytes=N-" with 206 unless told not to.</summary>
    private sealed class FakeServer(byte[] payload, bool honourRange = true, int failAfter = -1, Action? onFail = null) : HttpMessageHandler
    {
        public RangeItemHeaderValue? LastRange { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRange = request.Headers.Range?.Ranges.FirstOrDefault();
            var from = honourRange ? (int)(LastRange?.From ?? 0) : 0;
            var body = payload[from..];
            Stream stream = failAfter >= 0 ? new FailingStream(body, failAfter, onFail!) : new MemoryStream(body);

            var response = new HttpResponseMessage(honourRange && from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        }
    }

    /// <summary>Returns <c>failAfter</c> bytes, then triggers cancellation and throws as a cancelled read would.</summary>
    private sealed class FailingStream(byte[] data, int failAfter, Action onFail) : MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= failAfter)
            {
                onFail();
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException();
            }
            return await base.ReadAsync(buffer[..(int)Math.Min(buffer.Length, failAfter - Position)], cancellationToken);
        }
    }
}
