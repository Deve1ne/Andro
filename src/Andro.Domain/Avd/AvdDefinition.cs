namespace Andro.Domain.Avd;

/// <summary>The virtual phone. <see cref="SystemImageSubdirectory"/> is relative to the SDK root.</summary>
public sealed record AvdDefinition(
    string Name,
    string ApiLevel,
    string SystemImageSubdirectory,
    int RamMb,
    int Cores,
    int Width,
    int Height,
    int Density,
    string DataPartitionSize)
{
    public static AvdDefinition Default(string apiLevel, string systemImageSubdirectory) =>
        new("Andro", apiLevel, systemImageSubdirectory, 4096, 4, 1080, 2400, 420, "8G");
}
