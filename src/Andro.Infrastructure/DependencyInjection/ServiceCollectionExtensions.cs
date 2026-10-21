using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Application.Setup;
using Andro.Domain.Sdk;
using Andro.Infrastructure.Archives;
using Andro.Infrastructure.Downloads;
using Andro.Infrastructure.Prerequisites;
using Andro.Infrastructure.Processes;
using Andro.Infrastructure.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace Andro.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAndro(this IServiceCollection services, AndroPaths paths)
    {
        // Large system images: no overall timeout, cancellation is the caller's.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        services.AddSingleton(paths);
        services.AddSingleton(PinnedComponents.Current);
        services.AddSingleton<IManifestSource>(_ => new HttpManifestSource(http));
        services.AddSingleton<IDownloader>(_ => new HttpDownloader(http));
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();
        services.AddSingleton<ISetupStore, JsonSetupStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPrerequisiteChecker, PrerequisiteChecker>();
        services.AddSingleton<SetupService>();
        return services;
    }
}
