using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Application.Setup;
using Andro.Domain.Sdk;
using Andro.Infrastructure.Archives;
using Andro.Infrastructure.Downloads;
using Andro.Infrastructure.Emulator;
using Andro.Infrastructure.Prerequisites;
using Andro.Infrastructure.Processes;
using Andro.Infrastructure.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace Andro.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAndro(this IServiceCollection services, AndroPaths paths)
    {
        // Large system images: no overall timeout; HttpDownloader detects a stalled connection per read.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var manifestHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        services.AddSingleton(paths);
        services.AddSingleton(PinnedComponents.Current);
        services.AddSingleton<IManifestSource>(_ => new HttpManifestSource(manifestHttp));
        services.AddSingleton<IDownloader>(_ => new HttpDownloader(http));
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();
        services.AddSingleton<ISetupStore, JsonSetupStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPrerequisiteChecker, PrerequisiteChecker>();
        services.AddSingleton<IEmulatorLauncher, EmulatorLauncher>();
        services.AddSingleton<SetupService>();
        return services;
    }
}
