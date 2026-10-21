using Andro.Application.Paths;
using Andro.Infrastructure.Emulator;

namespace Andro.Infrastructure.Tests.Emulator;

public class EmulatorLauncherTests
{
    private static readonly AndroPaths Paths = new(@"C:\Users\Jérôme Dupont\AppData\Local\Deve1ne\Andro");

    [Fact]
    public void Start_info_sets_sdk_avd_and_user_home_env()
    {
        var info = EmulatorLauncher.BuildStartInfo(Paths, "Andro", 8554);

        Assert.Equal(Paths.Sdk, info.Environment["ANDROID_SDK_ROOT"]);
        Assert.Equal(Paths.Sdk, info.Environment["ANDROID_HOME"]);
        Assert.Equal(Paths.AvdHome, info.Environment["ANDROID_AVD_HOME"]);
        Assert.Equal(Paths.UserHome, info.Environment["ANDROID_USER_HOME"]);
        Assert.Equal(Paths.UserHome, info.Environment["ANDROID_EMULATOR_HOME"]);
    }

    [Fact]
    public void Start_info_runs_headless_with_grpc_port()
    {
        var info = EmulatorLauncher.BuildStartInfo(Paths, "Andro", 8554);

        Assert.Equal(["-avd", "Andro", "-no-window", "-no-metrics", "-grpc", "8554", "-grpc-use-token", "-gpu", "host", "-no-boot-anim"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        // Without a console qemu aborts (0x40000015): it gets a hidden one.
        Assert.False(info.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, info.WindowStyle);
        // Without console and std handles the emulator aborts (0x40000015): its output goes to logs\emulator.log.
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.True(info.RedirectStandardInput);
    }

    [Fact]
    public void Cold_boot_skips_snapshot_load()
    {
        var info = EmulatorLauncher.BuildStartInfo(Paths, "Andro", 8554, coldBoot: true);

        Assert.Contains("-no-snapshot-load", info.ArgumentList);
        Assert.DoesNotContain("-no-snapshot-load", EmulatorLauncher.BuildStartInfo(Paths, "Andro", 8554).ArgumentList);
    }

    [Fact]
    public void Start_info_handles_paths_with_spaces()
    {
        var info = EmulatorLauncher.BuildStartInfo(Paths, "Andro", 8554);

        Assert.Equal(@"C:\Users\Jérôme Dupont\AppData\Local\Deve1ne\Andro\sdk\emulator\emulator.exe", info.FileName);
        Assert.Empty(info.Arguments);
    }

    [Fact]
    public void Prepare_creates_user_home_the_emulator_writes_to()
    {
        var paths = new AndroPaths(Path.Combine(Path.GetTempPath(), "andro-prep-" + Guid.NewGuid(), "Jérôme Dupont"));
        try
        {
            EmulatorLauncher.PrepareDirectories(paths);

            Assert.True(Directory.Exists(paths.UserHome));
            Assert.True(Directory.Exists(paths.AvdHome));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(paths.Root)!, recursive: true);
        }
    }

    [Fact]
    public void Ascii_safe_turns_accented_root_into_existing_short_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "andro-8dot3-" + Guid.NewGuid().ToString("N")[..6], "Jérôme Dupont")).FullName;
        try
        {
            var safe = EmulatorLauncher.AsciiSafe(new AndroPaths(root));

            Assert.True(safe.Root.All(char.IsAscii), safe.Root);
            Assert.DoesNotContain(' ', safe.Root);
            File.WriteAllText(Path.Combine(safe.Root, "probe.txt"), "same folder");
            Assert.Equal("same folder", File.ReadAllText(Path.Combine(root, "probe.txt")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    [Fact]
    public void Ascii_safe_keeps_plain_root()
    {
        var root = Directory.CreateTempSubdirectory("andro-plain-").FullName;
        try
        {
            Assert.Equal(root, EmulatorLauncher.AsciiSafe(new AndroPaths(root)).Root);
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Discovery_file_is_matched_by_avd_folder_and_read()
    {
        var running = Directory.CreateTempSubdirectory("andro-running-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(running, "pid_1.ini"), "avd.dir=C:\\Other\\Phone.avd\ngrpc.token=nope\nport.serial=5556\n");
            File.WriteAllText(Path.Combine(running, "pid_2.ini"), $"port.serial=5554\navd.dir={Path.Combine(Paths.AvdHome, "Andro.avd")}\ngrpc.port=8554\ngrpc.token=secret=with=equals\n");

            var discovery = EmulatorLauncher.FindDiscovery(running, Path.Combine(Paths.AvdHome, "Andro.avd"));

            Assert.Equal(new EmulatorDiscovery("secret=with=equals", 5554), discovery);
        }
        finally
        {
            Directory.Delete(running, recursive: true);
        }
    }
}
