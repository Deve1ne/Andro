using Andro.Application.Paths;
using Andro.Application.Setup;
using Andro.Infrastructure.Setup;

namespace Andro.Infrastructure.Tests.Setup;

public sealed class JsonSetupStoreTests : IDisposable
{
    private readonly AndroPaths _paths = new(Path.Combine(Path.GetTempPath(), "andro-store-" + Guid.NewGuid(), "Jérôme Dupont"));

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_paths.Root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    [Fact]
    public async Task Missing_file_loads_empty_state()
    {
        var state = await new JsonSetupStore(_paths).LoadAsync();

        Assert.Empty(state.InstalledComponents);
        Assert.Empty(state.AcceptedLicenseHashes);
        Assert.False(state.AvdCreated);
    }

    [Fact]
    public async Task Round_trips_state_and_leaves_no_temp_file()
    {
        var state = new SetupState(
            new Dictionary<string, string> { ["android-sdk-license"] = "abc" },
            new Dictionary<string, string> { ["emulator"] = "c2" },
            AvdCreated: true);

        await new JsonSetupStore(_paths).SaveAsync(state);
        var loaded = await new JsonSetupStore(_paths).LoadAsync();

        Assert.Equal("abc", loaded.AcceptedLicenseHashes["android-sdk-license"]);
        Assert.Equal("c2", loaded.InstalledComponents["emulator"]);
        Assert.True(loaded.AvdCreated);
        Assert.Equal(["setup-state.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }
}
