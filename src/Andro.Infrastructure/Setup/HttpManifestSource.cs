using System.Xml.Linq;
using Andro.Application.Abstractions;
using Andro.Domain.Sdk;

namespace Andro.Infrastructure.Setup;

public sealed class HttpManifestSource(HttpClient http) : IManifestSource
{
    private static readonly Uri RepositoryUrl = new("https://dl.google.com/android/repository/repository2-3.xml");
    private static readonly Uri PlayStoreImagesUrl = new("https://dl.google.com/android/repository/sys-img/google_apis_playstore/sys-img2-3.xml");

    public async Task<(SdkManifest Repository, SdkManifest PlayStoreImages)> FetchAsync(CancellationToken ct)
    {
        var repository = FetchOneAsync(RepositoryUrl, ct);
        var images = FetchOneAsync(PlayStoreImagesUrl, ct);
        return (await repository, await images);
    }

    private async Task<SdkManifest> FetchOneAsync(Uri url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct);
        var xml = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        return SdkManifestParser.Parse(xml, url);
    }
}
