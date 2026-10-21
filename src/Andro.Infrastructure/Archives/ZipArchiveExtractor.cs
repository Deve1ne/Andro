using System.IO.Compression;
using Andro.Application.Abstractions;

namespace Andro.Infrastructure.Archives;

public sealed class ZipArchiveExtractor : IArchiveExtractor
{
    public Task ExtractAsync(string zipFile, string targetDirectory, CancellationToken ct) =>
        Task.Run(() => Extract(zipFile, Path.GetFullPath(targetDirectory), ct), ct);

    private static void Extract(string zipFile, string target, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        var staging = target + ".tmp";
        var stagingRoot = staging + Path.DirectorySeparatorChar;
        var prefix = SingleRootPrefix(zip);

        // Resolve every entry before writing anything: a single escaping entry rejects the whole archive.
        var files = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
            if (relative.Length == 0 || relative.EndsWith('/')) continue;

            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"L'archive {Path.GetFileName(zipFile)} contient un chemin interdit : {entry.FullName}");
            }
            files.Add((entry, destination));
        }

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        foreach (var (entry, destination) in files)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
        Directory.CreateDirectory(staging);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var previous = target + ".old";
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        if (Directory.Exists(target)) Directory.Move(target, previous);
        Directory.Move(staging, target);
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
    }

    /// <summary>"emulator/" when every entry sits under that one folder, otherwise "".</summary>
    private static string SingleRootPrefix(ZipArchive zip)
    {
        var roots = zip.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Select(name => name.Contains('/') ? name[..(name.IndexOf('/') + 1)] : name)
            .Distinct()
            .ToList();
        return roots.Count == 1 && roots[0].EndsWith('/') && roots[0] != "../" ? roots[0] : "";
    }
}
