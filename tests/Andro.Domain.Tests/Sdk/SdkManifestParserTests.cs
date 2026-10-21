using System.Xml.Linq;
using Andro.Domain.Sdk;

namespace Andro.Domain.Tests.Sdk;

public class SdkManifestParserTests
{
    private static readonly Uri RepositoryUrl = new("https://dl.google.com/android/repository/repository2-3.xml");

    private static SdkManifest Load(string fixture, Uri? url = null) =>
        SdkManifestParser.Parse(XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), url ?? RepositoryUrl);

    [Fact]
    public void Parses_package_path_revision_channel_and_licenses()
    {
        var package = Assert.Single(Load("repository-min.xml").Packages);

        Assert.Equal("emulator", package.Path);
        Assert.Equal(new SdkRevision(35, 1, 4), package.Revision);
        Assert.Equal("channel-0", package.Channel);
        Assert.Equal(["android-sdk-license"], package.LicenseRefs);
    }

    [Fact]
    public void Resolves_relative_archive_url_against_manifest_folder()
    {
        var manifest = Load("repository-min.xml", new Uri("https://dl.google.com/android/repository/sys-img/google_apis_playstore/sys-img2-3.xml"));

        var windows = manifest.Packages[0].Archives.Single(a => a.HostOs == "windows");

        Assert.Equal(new Uri("https://dl.google.com/android/repository/sys-img/google_apis_playstore/emulator-windows_x64-1.zip"), windows.Url);
    }

    [Fact]
    public void Keeps_absolute_archive_url()
    {
        var any = Load("repository-min.xml").Packages[0].Archives.Single(a => a.HostOs is null);

        Assert.Equal(new Uri("https://example.org/abs/emulator-any.zip"), any.Url);
    }

    [Fact]
    public void Prefers_sha256_when_both_checksums_present()
    {
        var any = Load("repository-min.xml").Packages[0].Archives.Single(a => a.HostOs is null);

        Assert.Equal("sha256", any.ChecksumType);
        Assert.Equal(new string('d', 64), any.Checksum);
        Assert.Equal(300, any.Size);
    }

    [Fact]
    public void Keeps_archives_per_host_os()
    {
        var archives = Load("repository-min.xml").Packages[0].Archives;

        Assert.Equal(["windows", "linux", null], archives.Select(a => a.HostOs));
        Assert.Equal("sha1", archives[0].ChecksumType);
        Assert.Equal(new string('a', 40), archives[0].Checksum);
    }

    [Fact]
    public void Reads_license_text_verbatim()
    {
        var license = Load("repository-min.xml").Licenses["android-sdk-license"];

        Assert.Equal("Terms and Conditions\n\n  Line with   spaces.", license.Text.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("35.1.4", 35, 1, 4)]
    [InlineData("37", 37, 0, 0)]
    [InlineData("37.0.1", 37, 0, 1)]
    public void Revision_parses_partial_versions(string text, int major, int minor, int micro)
    {
        Assert.Equal(new SdkRevision(major, minor, micro), SdkRevision.Parse(text));
    }

    [Fact]
    public void Revisions_compare_numerically()
    {
        Assert.True(SdkRevision.Parse("37.10.0").CompareTo(SdkRevision.Parse("37.9.9")) > 0);
    }
}
