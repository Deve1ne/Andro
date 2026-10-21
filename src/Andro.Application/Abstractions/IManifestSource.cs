using Andro.Domain.Sdk;

namespace Andro.Application.Abstractions;

public interface IManifestSource
{
    Task<(SdkManifest Repository, SdkManifest PlayStoreImages)> FetchAsync(CancellationToken ct);
}
