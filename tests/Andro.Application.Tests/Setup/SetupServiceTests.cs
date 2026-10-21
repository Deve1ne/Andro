using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Application.Setup;
using Andro.Domain.Sdk;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Andro.Application.Tests.Setup;

public sealed class SetupServiceTests : IDisposable
{
    private static readonly ComponentPins Pins = new(new SdkRevision(35, 1, 4), "37.0");
    private static readonly SdkLicense SdkTerms = new("android-sdk-license", "sdk terms");

    private readonly AndroPaths _paths = new(Directory.CreateTempSubdirectory("andro-setup-").FullName);
    private readonly IManifestSource _manifests = Substitute.For<IManifestSource>();
    private readonly IDownloader _downloader = Substitute.For<IDownloader>();
    private readonly IArchiveExtractor _extractor = Substitute.For<IArchiveExtractor>();
    private readonly InMemoryStore _store = new();

    public SetupServiceTests()
    {
        _manifests.FetchAsync(Arg.Any<CancellationToken>()).Returns(Manifests(SdkTerms));
    }

    public void Dispose() => Directory.Delete(_paths.Root, recursive: true);

    private static (SdkManifest, SdkManifest) Manifests(SdkLicense license, string imageChecksum = "c3")
    {
        SdkPackage Package(string path, string revision, string checksum) => new(path, SdkRevision.Parse(revision), "channel-0", [license.Id],
            [new SdkArchive(new Uri($"https://dl.google.com/android/repository/{checksum}.zip"), 10, "sha1", checksum, null)]);
        var licenses = new Dictionary<string, SdkLicense> { [license.Id] = license };
        return (new SdkManifest([Package("platform-tools", "37.0.1", "c1"), Package("emulator", "35.1.4", "c2")], licenses),
                new SdkManifest([Package(Pins.SystemImagePath, "6", imageChecksum)], licenses));
    }

    private SetupService Service() => new(_manifests, _store, _downloader, _extractor, _paths, Pins);

    private async Task InstallEverything()
    {
        var service = Service();
        var plan = await service.PlanAsync(default);
        await service.AcceptLicensesAsync(plan.LicensesToAccept);
        await service.RunAsync(plan, null, default);
    }

    [Fact]
    public async Task Plan_lists_all_components_and_licenses_on_first_run()
    {
        var plan = await Service().PlanAsync(default);

        Assert.Equal(["platform-tools", "emulator", Pins.SystemImagePath], plan.Pending.Select(c => c.PackagePath));
        Assert.Equal([SdkTerms], plan.LicensesToAccept);
        Assert.Equal(30, plan.DownloadBytes);
    }

    [Fact]
    public async Task Plan_skips_installed_components_at_same_revision()
    {
        await InstallEverything();

        var plan = await Service().PlanAsync(default);

        Assert.Empty(plan.Pending);
        Assert.Empty(plan.LicensesToAccept);
    }

    [Fact]
    public async Task Plan_reinstalls_component_whose_archive_changed()
    {
        await InstallEverything();
        _manifests.FetchAsync(Arg.Any<CancellationToken>()).Returns(Manifests(SdkTerms, imageChecksum: "c3-new"));

        var plan = await Service().PlanAsync(default);

        Assert.Equal([Pins.SystemImagePath], plan.Pending.Select(c => c.PackagePath));
    }

    [Fact]
    public async Task Nothing_downloaded_without_acceptance()
    {
        var service = Service();
        var plan = await service.PlanAsync(default);

        await Assert.ThrowsAsync<LicenseNotAcceptedException>(() => service.RunAsync(plan, null, default));

        await _downloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task New_license_requires_new_acceptance()
    {
        await InstallEverything();
        var changed = SdkTerms with { Text = "sdk terms, version 2" };
        _manifests.FetchAsync(Arg.Any<CancellationToken>()).Returns(Manifests(changed, imageChecksum: "c3-new"));

        var plan = await Service().PlanAsync(default);

        Assert.Equal([changed], plan.LicensesToAccept);
        await Assert.ThrowsAsync<LicenseNotAcceptedException>(() => Service().RunAsync(plan, null, default));
    }

    [Fact]
    public async Task Interrupted_component_is_not_marked_installed()
    {
        _extractor.ExtractAsync(Arg.Any<string>(), Arg.Is<string>(t => t.Contains("system-images")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Espace disque insuffisant"));
        var service = Service();
        var plan = await service.PlanAsync(default);
        await service.AcceptLicensesAsync(plan.LicensesToAccept);

        await Assert.ThrowsAsync<IOException>(() => service.RunAsync(plan, null, default));

        Assert.False((await _store.LoadAsync()).InstalledComponents.ContainsKey(Pins.SystemImagePath));
        Assert.Equal([Pins.SystemImagePath], (await Service().PlanAsync(default)).Pending.Select(c => c.PackagePath));
    }

    [Fact]
    public async Task Extracts_each_component_to_its_sdk_subdirectory()
    {
        await InstallEverything();

        await _extractor.Received().ExtractAsync(Path.Combine(_paths.Downloads, "c2.zip"), Path.Combine(_paths.Sdk, "emulator"), Arg.Any<CancellationToken>());
        await _extractor.Received().ExtractAsync(Arg.Any<string>(), Path.Combine(_paths.Sdk, @"system-images\android-37.0\google_apis_playstore\x86_64"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creates_platforms_folder_and_avd_once()
    {
        await InstallEverything();
        var config = Path.Combine(_paths.AvdHome, "Andro.avd", "config.ini");
        File.AppendAllText(config, "user.edit=1\n");

        await InstallEverything();

        Assert.True(Directory.Exists(Path.Combine(_paths.Sdk, "platforms")));
        Assert.True(File.Exists(Path.Combine(_paths.AvdHome, "Andro.ini")));
        Assert.Contains("user.edit=1", File.ReadAllText(config));
        Assert.True((await _store.LoadAsync()).AvdCreated);
    }

    [Fact]
    public async Task Reports_phases_in_order()
    {
        var reports = new List<SetupProgress>();
        var service = Service();
        var plan = await service.PlanAsync(default);
        await service.AcceptLicensesAsync(plan.LicensesToAccept);

        await service.RunAsync(plan, new SyncProgress(reports.Add), default);

        var phases = reports.Select(r => $"{r.Phase}:{r.PackagePath}").Distinct().ToList();
        Assert.Equal([
            "download:platform-tools", "extract:platform-tools",
            "download:emulator", "extract:emulator",
            $"download:{Pins.SystemImagePath}", $"extract:{Pins.SystemImagePath}",
            "avd:Andro"], phases);
    }

    private sealed class SyncProgress(Action<SetupProgress> report) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value) => report(value);
    }

    private sealed class InMemoryStore : ISetupStore
    {
        private SetupState _state = SetupState.Empty;

        public Task<SetupState> LoadAsync() => Task.FromResult(_state);

        public Task SaveAsync(SetupState state)
        {
            _state = state;
            return Task.CompletedTask;
        }
    }
}
