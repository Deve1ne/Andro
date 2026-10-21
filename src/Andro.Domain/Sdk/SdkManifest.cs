namespace Andro.Domain.Sdk;

public sealed record SdkLicense(string Id, string Text);

/// <summary>One downloadable file of a package. <see cref="ChecksumType"/> is "sha256" or "sha1".</summary>
public sealed record SdkArchive(Uri Url, long Size, string ChecksumType, string Checksum, string? HostOs);

public sealed record SdkRevision(int Major, int Minor, int Micro) : IComparable<SdkRevision>
{
    public static SdkRevision Parse(string text)
    {
        var parts = text.Split('.');
        int At(int i) => i < parts.Length ? int.Parse(parts[i]) : 0;
        return new SdkRevision(At(0), At(1), At(2));
    }

    public int CompareTo(SdkRevision? other)
    {
        if (other is null) return 1;
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Micro.CompareTo(other.Micro);
    }

    public override string ToString() => $"{Major}.{Minor}.{Micro}";
}

public sealed record SdkPackage(string Path, SdkRevision Revision, string Channel, IReadOnlyList<string> LicenseRefs, IReadOnlyList<SdkArchive> Archives);

public sealed record SdkManifest(IReadOnlyList<SdkPackage> Packages, IReadOnlyDictionary<string, SdkLicense> Licenses);
