using System.Text.Json;
using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Application.Setup;

namespace Andro.Infrastructure.Setup;

/// <summary>Stores <see cref="SetupState"/> in &lt;Root&gt;\setup-state.json, written atomically.</summary>
public sealed class JsonSetupStore(AndroPaths paths) : ISetupStore
{
    private string File => Path.Combine(paths.Root, "setup-state.json");

    public async Task<SetupState> LoadAsync()
    {
        if (!System.IO.File.Exists(File)) return SetupState.Empty;

        await using var stream = System.IO.File.OpenRead(File);
        var dto = await JsonSerializer.DeserializeAsync<Dto>(stream);
        return dto is null
            ? SetupState.Empty
            : new SetupState(dto.AcceptedLicenseHashes ?? [], dto.InstalledComponents ?? [], dto.AvdCreated);
    }

    public async Task SaveAsync(SetupState state)
    {
        Directory.CreateDirectory(paths.Root);
        var temp = File + ".tmp";
        await using (var stream = System.IO.File.Create(temp))
        {
            var dto = new Dto(new(state.AcceptedLicenseHashes), new(state.InstalledComponents), state.AvdCreated);
            await JsonSerializer.SerializeAsync(stream, dto, new JsonSerializerOptions { WriteIndented = true });
        }
        System.IO.File.Move(temp, File, overwrite: true);
    }

    private sealed record Dto(Dictionary<string, string>? AcceptedLicenseHashes, Dictionary<string, string>? InstalledComponents, bool AvdCreated);
}
