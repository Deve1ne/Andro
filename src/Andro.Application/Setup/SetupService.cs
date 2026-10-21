using System.Security.Cryptography;
using System.Text;
using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Domain.Avd;
using Andro.Domain.Sdk;

namespace Andro.Application.Setup;

/// <summary>Brings the Android environment from nothing to a ready virtual phone, resumably.</summary>
public sealed class SetupService(
    IManifestSource manifests,
    ISetupStore store,
    IDownloader downloader,
    IArchiveExtractor extractor,
    AndroPaths paths,
    ComponentPins pins)
{
    public async Task<SetupPlan> PlanAsync(CancellationToken ct)
    {
        var (repository, images) = await manifests.FetchAsync(ct);
        var state = await store.LoadAsync();

        var pending = ComponentSelector.Select(repository, images, pins)
            .Where(c => !state.InstalledComponents.TryGetValue(c.PackagePath, out var installed) || installed != c.Archive.Checksum)
            .ToList();

        var licenses = pending
            .SelectMany(c => c.Licenses)
            .DistinctBy(l => l.Id)
            .Where(l => !IsAccepted(state, l))
            .ToList();

        return new SetupPlan(pending, licenses, pending.Sum(c => c.Archive.Size));
    }

    public async Task AcceptLicensesAsync(IReadOnlyList<SdkLicense> licenses)
    {
        var state = await store.LoadAsync();
        var accepted = new Dictionary<string, string>(state.AcceptedLicenseHashes);
        foreach (var license in licenses) accepted[license.Id] = Hash(license.Text);
        await store.SaveAsync(state with { AcceptedLicenseHashes = accepted });
    }

    public async Task RunAsync(SetupPlan plan, IProgress<SetupProgress>? progress, CancellationToken ct)
    {
        var state = await store.LoadAsync();
        var refused = plan.Pending.SelectMany(c => c.Licenses).DistinctBy(l => l.Id).Where(l => !IsAccepted(state, l)).ToList();
        if (refused.Count > 0)
        {
            throw new LicenseNotAcceptedException($"Licence non acceptée : {string.Join(", ", refused.Select(l => l.Id))}.");
        }

        foreach (var component in plan.Pending)
        {
            var archive = Path.Combine(paths.Downloads, component.Archive.Url.Segments[^1]);
            var size = component.Archive.Size;

            progress?.Report(new SetupProgress(component.PackagePath, "download", 0, size));
            var downloadProgress = progress is null ? null : new Progress(p => progress.Report(new SetupProgress(component.PackagePath, "download", p.Received, p.Total)));
            await downloader.DownloadAsync(component.Archive, archive, downloadProgress, ct);

            progress?.Report(new SetupProgress(component.PackagePath, "extract", 0, size));
            await extractor.ExtractAsync(archive, Path.Combine(paths.Sdk, component.InstallSubdirectory), ct);

            // Only a fully extracted component counts as installed: an interruption above leaves it pending.
            state = await store.LoadAsync();
            var installed = new Dictionary<string, string>(state.InstalledComponents) { [component.PackagePath] = component.Archive.Checksum };
            state = state with { InstalledComponents = installed };
            await store.SaveAsync(state);
            if (File.Exists(archive)) File.Delete(archive);
        }

        // The emulator rejects an SDK root without a platforms folder.
        Directory.CreateDirectory(Path.Combine(paths.Sdk, "platforms"));

        if (!state.AvdCreated)
        {
            var avd = AvdDefinition.Default(pins.ApiLevel, pins.SystemImagePath.Replace(';', '\\'));
            progress?.Report(new SetupProgress(avd.Name, "avd", 0, 1));
            foreach (var (relative, content) in AvdFiles.Render(avd, paths.AvdHome))
            {
                var file = Path.Combine(paths.AvdHome, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            }
            await store.SaveAsync(state with { AvdCreated = true });
        }
    }

    private static bool IsAccepted(SetupState state, SdkLicense license) =>
        state.AcceptedLicenseHashes.TryGetValue(license.Id, out var hash) && hash == Hash(license.Text);

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")))).ToLowerInvariant();

    private sealed class Progress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
