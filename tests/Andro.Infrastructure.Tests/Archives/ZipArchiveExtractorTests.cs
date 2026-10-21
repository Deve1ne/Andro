using System.IO.Compression;
using Andro.Infrastructure.Archives;

namespace Andro.Infrastructure.Tests.Archives;

public sealed class ZipArchiveExtractorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("andro-zip-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Target => Path.Combine(_dir, "sdk", "emulator");

    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task Strips_single_top_level_folder()
    {
        var zip = Zip(("emulator/emulator.exe", "exe"), ("emulator/lib/a.proto", "proto"));

        await new ZipArchiveExtractor().ExtractAsync(zip, Target, default);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(Target, "emulator.exe")));
        Assert.Equal("proto", File.ReadAllText(Path.Combine(Target, "lib", "a.proto")));
    }

    [Fact]
    public async Task Keeps_layout_when_no_single_root()
    {
        var zip = Zip(("a.txt", "a"), ("dir/b.txt", "b"));

        await new ZipArchiveExtractor().ExtractAsync(zip, Target, default);

        Assert.True(File.Exists(Path.Combine(Target, "a.txt")));
        Assert.True(File.Exists(Path.Combine(Target, "dir", "b.txt")));
    }

    [Fact]
    public async Task Rejects_entries_escaping_target()
    {
        var zip = Zip(("root/ok.txt", "ok"), ("../evil.txt", "evil"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new ZipArchiveExtractor().ExtractAsync(zip, Target, default));

        Assert.Empty(Directory.GetFiles(_dir, "evil.txt", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task Replaces_existing_target_atomically()
    {
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "old.txt"), "old");
        var zip = Zip(("emulator/new.txt", "new"));

        await new ZipArchiveExtractor().ExtractAsync(zip, Target, default);

        Assert.False(File.Exists(Path.Combine(Target, "old.txt")));
        Assert.True(File.Exists(Path.Combine(Target, "new.txt")));
        Assert.False(Directory.Exists(Target + ".tmp"));
    }
}
