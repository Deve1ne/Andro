namespace Andro.Domain.Sdk;

/// <summary>A package to download and extract under the SDK root.</summary>
public sealed record SetupComponent(string PackagePath, string InstallSubdirectory, SdkArchive Archive, IReadOnlyList<SdkLicense> Licenses);

public sealed class ComponentSelectionException(string message) : Exception(message);

public static class ComponentSelector
{
    private const string StableChannel = "channel-0";

    /// <summary>Picks platform-tools (latest stable), the pinned emulator and the pinned system image, in that order.</summary>
    public static IReadOnlyList<SetupComponent> Select(SdkManifest repository, SdkManifest playStoreImages, ComponentPins pins)
    {
        var platformTools = repository.Packages
            .Where(p => p.Path == "platform-tools" && p.Channel == StableChannel)
            .MaxBy(p => p.Revision)
            ?? throw new ComponentSelectionException("Aucune version stable de platform-tools dans le manifeste Google.");

        var emulator = repository.Packages
            .FirstOrDefault(p => p.Path == "emulator" && p.Revision.Equals(pins.EmulatorRevision))
            ?? throw new ComponentSelectionException($"L'émulateur {pins.EmulatorRevision} n'est plus proposé par Google.");

        var image = playStoreImages.Packages
            .Where(p => p.Path == pins.SystemImagePath)
            .MaxBy(p => p.Revision)
            ?? throw new ComponentSelectionException($"L'image {pins.SystemImagePath} n'est plus proposée par Google.");

        return [ToComponent(platformTools, repository), ToComponent(emulator, repository), ToComponent(image, playStoreImages)];
    }

    private static SetupComponent ToComponent(SdkPackage package, SdkManifest manifest)
    {
        var archive = package.Archives.FirstOrDefault(a => a.HostOs == "windows")
            ?? package.Archives.FirstOrDefault(a => a.HostOs is null)
            ?? throw new ComponentSelectionException($"Pas d'archive Windows pour {package.Path}.");

        var licenses = package.LicenseRefs
            .Select(id => manifest.Licenses.TryGetValue(id, out var license) ? license : throw new ComponentSelectionException($"Licence {id} introuvable."))
            .ToList();

        return new SetupComponent(package.Path, package.Path.Replace(';', '\\'), archive, licenses);
    }
}
