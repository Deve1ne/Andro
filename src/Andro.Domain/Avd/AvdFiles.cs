using System.Text;

namespace Andro.Domain.Avd;

/// <summary>Renders the files avdmanager would create, so no Java is needed.</summary>
public static class AvdFiles
{
    /// <summary>Keys are paths relative to <paramref name="avdHome"/>; values are UTF-8 file contents.</summary>
    public static IReadOnlyDictionary<string, string> Render(AvdDefinition avd, string avdHome)
    {
        var folder = $"{avd.Name}.avd";

        var ini = Lines(
            ("avd.ini.encoding", "UTF-8"),
            ("path", Path.Combine(avdHome, folder)),
            ("path.rel", $@"avd\{folder}"),
            ("target", $"android-{avd.ApiLevel}"));

        var config = Lines(
            ("AvdId", avd.Name),
            ("avd.ini.displayname", avd.Name),
            ("avd.ini.encoding", "UTF-8"),
            ("PlayStore.enabled", "true"),
            ("abi.type", "x86_64"),
            ("hw.cpu.arch", "x86_64"),
            ("hw.cpu.ncore", avd.Cores.ToString()),
            ("hw.ramSize", avd.RamMb.ToString()),
            ("hw.lcd.width", avd.Width.ToString()),
            ("hw.lcd.height", avd.Height.ToString()),
            ("hw.lcd.density", avd.Density.ToString()),
            ("hw.keyboard", "yes"),
            ("hw.gpu.enabled", "yes"),
            ("hw.gpu.mode", "auto"),
            ("image.sysdir.1", avd.SystemImageSubdirectory.TrimEnd('\\') + @"\"),
            ("tag.id", "google_apis_playstore"),
            ("tag.display", "Google Play"),
            ("disk.dataPartition.size", avd.DataPartitionSize),
            ("fastboot.forceColdBoot", "no"),
            ("showDeviceFrame", "no"));

        return new Dictionary<string, string>
        {
            [$"{avd.Name}.ini"] = ini,
            [$@"{folder}\config.ini"] = config,
        };
    }

    private static string Lines(params (string Key, string Value)[] entries)
    {
        var text = new StringBuilder();
        foreach (var (key, value) in entries)
        {
            text.Append(key).Append('=').Append(value).Append('\n');
        }
        return text.ToString();
    }
}
