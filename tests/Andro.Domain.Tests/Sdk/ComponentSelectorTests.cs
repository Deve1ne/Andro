using Andro.Domain.Sdk;

namespace Andro.Domain.Tests.Sdk;

public class ComponentSelectorTests
{
    private static readonly ComponentPins Pins = new(new SdkRevision(35, 1, 4), "37.0");
    private const string ImagePath = "system-images;android-37.0;google_apis_playstore;x86_64";

    private static readonly SdkLicense SdkLicense = new("android-sdk-license", "sdk terms");
    private static readonly SdkLicense PreviewLicense = new("android-sdk-preview-license", "preview terms");

    private static SdkArchive Archive(string name, string? host = "windows") =>
        new(new Uri($"https://dl.google.com/android/repository/{name}"), 10, "sha1", new string('a', 40), host);

    private static SdkPackage Package(string path, string revision, string channel = "channel-0", string license = "android-sdk-license", params SdkArchive[] archives) =>
        new(path, SdkRevision.Parse(revision), channel, [license], archives.Length > 0 ? archives : [Archive($"{path}-{revision}.zip")]);

    private static SdkManifest Manifest(params SdkPackage[] packages) =>
        new(packages, new Dictionary<string, SdkLicense> { [SdkLicense.Id] = SdkLicense, [PreviewLicense.Id] = PreviewLicense });

    private static SdkManifest DefaultRepository() => Manifest(
        Package("platform-tools", "35.0.1"),
        Package("emulator", "35.1.4"));

    private static SdkManifest DefaultImages() => Manifest(
        Package(ImagePath, "6", archives: Archive("x86_64-37.0_r06.zip", host: null)));

    [Fact]
    public void Returns_platform_tools_emulator_then_image()
    {
        var selected = ComponentSelector.Select(DefaultRepository(), DefaultImages(), Pins);

        Assert.Equal(["platform-tools", "emulator", ImagePath], selected.Select(c => c.PackagePath));
    }

    [Fact]
    public void Selects_pinned_emulator_not_latest()
    {
        var repository = Manifest(Package("platform-tools", "35.0.1"), Package("emulator", "36.0.0"), Package("emulator", "35.1.4"));

        var emulator = ComponentSelector.Select(repository, DefaultImages(), Pins).Single(c => c.PackagePath == "emulator");

        Assert.EndsWith("emulator-35.1.4.zip", emulator.Archive.Url.AbsoluteUri);
    }

    [Fact]
    public void Selects_latest_stable_platform_tools()
    {
        var repository = Manifest(
            Package("platform-tools", "34.0.0"),
            Package("platform-tools", "35.0.1"),
            Package("platform-tools", "36.0.0", channel: "channel-1"),
            Package("emulator", "35.1.4"));

        var tools = ComponentSelector.Select(repository, DefaultImages(), Pins).Single(c => c.PackagePath == "platform-tools");

        Assert.EndsWith("platform-tools-35.0.1.zip", tools.Archive.Url.AbsoluteUri);
    }

    [Fact]
    public void Ignores_image_variants_of_the_pinned_api_level()
    {
        var images = Manifest(
            Package("system-images;android-37.0;google_apis_playstore_ps16k;x86_64", "7"),
            Package("system-images;android-37.0-ext20;google_apis_playstore;x86_64", "1"),
            Package(ImagePath, "6"),
            Package(ImagePath, "5"));

        var image = ComponentSelector.Select(DefaultRepository(), images, Pins).Last();

        Assert.Equal(ImagePath, image.PackagePath);
        Assert.EndsWith($"{ImagePath}-6.zip", image.Archive.Url.AbsoluteUri);
    }

    [Fact]
    public void Missing_pinned_image_throws()
    {
        var images = Manifest(Package("system-images;android-36;google_apis_playstore;x86_64", "7"));

        var error = Assert.Throws<ComponentSelectionException>(() => ComponentSelector.Select(DefaultRepository(), images, Pins));
        Assert.Contains("android-37.0", error.Message);
    }

    [Fact]
    public void Missing_pinned_emulator_throws()
    {
        var repository = Manifest(Package("platform-tools", "35.0.1"), Package("emulator", "36.0.0"));

        var error = Assert.Throws<ComponentSelectionException>(() => ComponentSelector.Select(repository, DefaultImages(), Pins));
        Assert.Contains("35.1.4", error.Message);
    }

    [Fact]
    public void Picks_windows_archive_and_maps_install_subdirectory()
    {
        var repository = Manifest(
            Package("platform-tools", "35.0.1"),
            Package("emulator", "35.1.4", archives: [Archive("emu-linux.zip", "linux"), Archive("emu-mac.zip", "macosx"), Archive("emu-win.zip", "windows")]));

        var selected = ComponentSelector.Select(repository, DefaultImages(), Pins);

        Assert.EndsWith("emu-win.zip", selected.Single(c => c.PackagePath == "emulator").Archive.Url.AbsoluteUri);
        Assert.Equal(@"system-images\android-37.0\google_apis_playstore\x86_64", selected.Last().InstallSubdirectory);
        Assert.Equal("emulator", selected.Single(c => c.PackagePath == "emulator").InstallSubdirectory);
    }

    [Fact]
    public void Package_without_windows_or_generic_archive_throws()
    {
        var repository = Manifest(Package("platform-tools", "35.0.1"), Package("emulator", "35.1.4", archives: Archive("emu-linux.zip", "linux")));

        Assert.Throws<ComponentSelectionException>(() => ComponentSelector.Select(repository, DefaultImages(), Pins));
    }

    [Fact]
    public void Collects_all_referenced_licenses()
    {
        var images = Manifest(new SdkPackage(ImagePath, SdkRevision.Parse("6"), "channel-0", ["android-sdk-license", "android-sdk-preview-license"], [Archive("img.zip", null)]));

        var image = ComponentSelector.Select(DefaultRepository(), images, Pins).Last();

        Assert.Equal(["sdk terms", "preview terms"], image.Licenses.Select(l => l.Text));
    }

    [Fact]
    public void Current_pins_match_spike_report()
    {
        Assert.Equal(new SdkRevision(37, 2, 12), PinnedComponents.Current.EmulatorRevision);
        Assert.Equal("37.0", PinnedComponents.Current.ApiLevel);
        Assert.Equal(ImagePath, PinnedComponents.Current.SystemImagePath);
    }
}
