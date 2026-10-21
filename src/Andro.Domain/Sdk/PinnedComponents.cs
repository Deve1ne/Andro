namespace Andro.Domain.Sdk;

/// <summary>The emulator revision and system image Andro was tested with.</summary>
public sealed record ComponentPins(SdkRevision EmulatorRevision, string ApiLevel)
{
    public string SystemImagePath => $"system-images;android-{ApiLevel};google_apis_playstore;x86_64";
}

public static class PinnedComponents
{
    // Verified by the display spike (docs/superpowers/spikes/2026-10-20-display-spike.md):
    // gRPC client generated from this emulator's proto; this image ships ARM64 translation.
    public static readonly ComponentPins Current = new(new SdkRevision(37, 2, 12), "37.0");
}
