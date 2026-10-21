using Andro.Domain.Avd;

namespace Andro.Domain.Tests.Avd;

public class AvdFilesTests
{
    private const string AvdHome = @"C:\Data\Andro\avd";
    private const string SysImg = @"system-images\android-37.0\google_apis_playstore\x86_64";

    private static Dictionary<string, string> Config(IReadOnlyDictionary<string, string> files) =>
        files[@"Andro.avd\config.ini"].ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

    private static IReadOnlyDictionary<string, string> Render(string avdHome = AvdHome) =>
        AvdFiles.Render(AvdDefinition.Default("37.0", SysImg), avdHome);

    [Fact]
    public void Default_definition_uses_spec_values()
    {
        var avd = AvdDefinition.Default("37.0", SysImg);

        Assert.Equal(new AvdDefinition("Andro", "37.0", SysImg, 4096, 4, 1080, 2400, 420, "8G"), avd);
    }

    [Fact]
    public void Renders_ini_pointing_to_avd_folder()
    {
        var ini = Render()["Andro.ini"].ReplaceLineEndings("\n");

        Assert.Equal("avd.ini.encoding=UTF-8\npath=C:\\Data\\Andro\\avd\\Andro.avd\npath.rel=avd\\Andro.avd\ntarget=android-37.0\n", ini);
    }

    [Fact]
    public void Config_enables_play_store_and_x86_64()
    {
        var config = Config(Render());

        Assert.Equal("true", config["PlayStore.enabled"]);
        Assert.Equal("x86_64", config["abi.type"]);
        Assert.Equal("x86_64", config["hw.cpu.arch"]);
        Assert.Equal("google_apis_playstore", config["tag.id"]);
        Assert.Equal("4096", config["hw.ramSize"]);
        Assert.Equal("4", config["hw.cpu.ncore"]);
        Assert.Equal("1080", config["hw.lcd.width"]);
        Assert.Equal("2400", config["hw.lcd.height"]);
        Assert.Equal("420", config["hw.lcd.density"]);
        Assert.Equal("8G", config["disk.dataPartition.size"]);
        Assert.Equal("no", config["fastboot.forceColdBoot"]);
    }

    [Fact]
    public void Config_uses_relative_sysdir_with_trailing_backslash()
    {
        Assert.Equal(@"system-images\android-37.0\google_apis_playstore\x86_64\", Config(Render())["image.sysdir.1"]);
    }

    [Fact]
    public void Config_paths_survive_spaces_and_accents()
    {
        const string home = @"C:\Users\Jérôme Dupont\AppData\Local\Deve1ne\Andro\avd";

        var ini = Render(home)["Andro.ini"];

        Assert.Contains($"path={home}\\Andro.avd\n", ini.ReplaceLineEndings("\n"));
    }
}
