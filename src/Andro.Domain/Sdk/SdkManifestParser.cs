using System.Xml.Linq;

namespace Andro.Domain.Sdk;

/// <summary>
/// Reads Google's SDK repository manifests (repository2-*.xml, sys-img2-*.xml).
/// Elements are matched by local name: the namespaces change with each schema version.
/// </summary>
public static class SdkManifestParser
{
    public static SdkManifest Parse(XDocument xml, Uri manifestUrl)
    {
        var root = xml.Root ?? throw new FormatException("Empty SDK manifest.");

        var licenses = root.Elements()
            .Where(e => e.Name.LocalName == "license")
            .Select(e => new SdkLicense((string)e.Attribute("id")!, e.Value))
            .ToDictionary(l => l.Id);

        var packages = root.Elements()
            .Where(e => e.Name.LocalName == "remotePackage")
            .Select(e => ParsePackage(e, manifestUrl))
            .ToList();

        return new SdkManifest(packages, licenses);
    }

    private static SdkPackage ParsePackage(XElement package, Uri manifestUrl)
    {
        var revision = Child(package, "revision");
        var version = string.Join('.', revision?.Elements().Select(e => e.Value) ?? ["0"]);

        return new SdkPackage(
            (string)package.Attribute("path")!,
            SdkRevision.Parse(version),
            (string?)Child(package, "channelRef")?.Attribute("ref") ?? "channel-0",
            package.Elements().Where(e => e.Name.LocalName == "uses-license").Select(e => (string)e.Attribute("ref")!).ToList(),
            package.Descendants().Where(e => e.Name.LocalName == "archive").Select(a => ParseArchive(a, manifestUrl)).ToList());
    }

    private static SdkArchive ParseArchive(XElement archive, Uri manifestUrl)
    {
        var complete = Child(archive, "complete") ?? throw new FormatException("Archive without <complete>.");
        var checksums = complete.Elements().Where(e => e.Name.LocalName == "checksum").ToList();
        var checksum = checksums.FirstOrDefault(c => (string?)c.Attribute("type") == "sha256")
            ?? checksums.FirstOrDefault()
            ?? throw new FormatException("Archive without checksum.");

        return new SdkArchive(
            new Uri(manifestUrl, Child(complete, "url")!.Value.Trim()),
            long.Parse(Child(complete, "size")!.Value),
            (string?)checksum.Attribute("type") ?? "sha1",
            checksum.Value.Trim().ToLowerInvariant(),
            Child(archive, "host-os")?.Value.Trim());
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
}
