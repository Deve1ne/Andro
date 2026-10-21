namespace Andro.Application.Abstractions;

public interface IArchiveExtractor
{
    /// <summary>
    /// Extracts <paramref name="zipFile"/> into <paramref name="targetDirectory"/>, dropping the archive's
    /// single top-level folder, and replaces any previous content of the target in one move.
    /// </summary>
    Task ExtractAsync(string zipFile, string targetDirectory, CancellationToken ct);
}
