using Andro.Domain.Sdk;

namespace Andro.Application.Setup;

/// <summary>
/// What is already done on this machine. <see cref="AcceptedLicenseHashes"/>: license id → SHA-256 of the text the
/// user accepted. <see cref="InstalledComponents"/>: package path → checksum of the archive that was installed.
/// </summary>
public sealed record SetupState(
    IReadOnlyDictionary<string, string> AcceptedLicenseHashes,
    IReadOnlyDictionary<string, string> InstalledComponents,
    bool AvdCreated)
{
    public static SetupState Empty { get; } = new(new Dictionary<string, string>(), new Dictionary<string, string>(), false);
}

public sealed record SetupPlan(IReadOnlyList<SetupComponent> Pending, IReadOnlyList<SdkLicense> LicensesToAccept, long DownloadBytes);

/// <summary><see cref="Phase"/> is "download", "extract" or "avd".</summary>
public sealed record SetupProgress(string PackagePath, string Phase, long Received, long Total);

public sealed class LicenseNotAcceptedException(string message) : Exception(message);
