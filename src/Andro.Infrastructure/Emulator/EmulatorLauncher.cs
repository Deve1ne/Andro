using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Andro.Application.Abstractions;
using Andro.Application.Paths;

namespace Andro.Infrastructure.Emulator;

public sealed record EmulatorDiscovery(string GrpcToken, int ConsolePort);

public sealed class EmulatorLauncher(AndroPaths paths, IProcessRunner runner) : IEmulatorLauncher
{
    // First cold boot measured at 213 s in the display spike; snapshot boots take ~15 s.
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(420);

    private AndroPaths? _runtime;

    /// <summary>The paths as handed to the emulator and adb (see <see cref="AsciiSafe"/>).</summary>
    private AndroPaths Runtime => _runtime ??= AsciiSafe(paths);

    public static ProcessStartInfo BuildStartInfo(AndroPaths paths, string avdName, int grpcPort, bool coldBoot = false)
    {
        var info = new ProcessStartInfo(Path.Combine(paths.Sdk, "emulator", "emulator.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.Combine(paths.Sdk, "emulator"),
        };
        // -no-metrics: no telemetry, and without it a fresh ANDROID_EMULATOR_HOME makes the headless emulator
        // stop on the consent question.
        foreach (var argument in new[] { "-avd", avdName, "-no-window", "-no-metrics", "-grpc", grpcPort.ToString(), "-grpc-use-token", "-gpu", "host", "-no-boot-anim" })
        {
            info.ArgumentList.Add(argument);
        }
        if (coldBoot) info.ArgumentList.Add("-no-snapshot-load");
        foreach (var (name, value) in AndroidEnvironment(paths)) info.Environment[name] = value;
        return info;
    }

    /// <summary>
    /// The emulator ignores ANDROID_USER_HOME for some of its files: ANDROID_EMULATOR_HOME is needed too
    /// to keep everything out of %USERPROFILE%\.android (display spike, finding 5).
    /// </summary>
    public static IReadOnlyDictionary<string, string> AndroidEnvironment(AndroPaths paths) => new Dictionary<string, string>
    {
        ["ANDROID_SDK_ROOT"] = paths.Sdk,
        ["ANDROID_HOME"] = paths.Sdk,
        ["ANDROID_AVD_HOME"] = paths.AvdHome,
        ["ANDROID_USER_HOME"] = paths.UserHome,
        ["ANDROID_EMULATOR_HOME"] = paths.UserHome,
        ["TEMP"] = paths.Temp,
        ["TMP"] = paths.Temp,
        ["ANDROID_TMP"] = paths.Temp,
    };

    /// <summary>
    /// The emulator breaks on non-ASCII paths (no discovery file, adb stays offline), e.g. a Windows user named
    /// "Jérôme": it is given the 8.3 short form of the data root instead, which is ASCII.
    /// </summary>
    public static AndroPaths AsciiSafe(AndroPaths paths)
    {
        var root = Path.GetFullPath(paths.Root);
        if (root.All(c => char.IsAscii(c) && c != ' ')) return paths;

        Directory.CreateDirectory(root);
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(root, buffer, buffer.Capacity);
        var shortRoot = length > 0 && length < buffer.Capacity ? buffer.ToString() : root;
        if (!shortRoot.All(char.IsAscii))
        {
            throw new InvalidOperationException(
                $"Le dossier de données « {root} » contient des caractères que l'émulateur Android ne gère pas, " +
                "et Windows n'en fournit pas de nom court (noms 8.3 désactivés sur ce disque).");
        }
        return new AndroPaths(shortRoot);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    /// <summary>The emulator fails (Windows error 3) when ANDROID_EMULATOR_HOME does not exist yet.</summary>
    public static void PrepareDirectories(AndroPaths paths)
    {
        Directory.CreateDirectory(paths.UserHome);
        Directory.CreateDirectory(paths.AvdHome);
        Directory.CreateDirectory(paths.Temp);
    }

    /// <summary>
    /// Where the emulator writes running\pid_*.ini. Observed with 37.2.12: %LOCALAPPDATA%\Temp, even when TEMP is
    /// redirected (TEMP only moves its scratch files).
    /// </summary>
    public static string DiscoveryDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "avd", "running");

    /// <summary>
    /// Reads the emulator's discovery file (running\pid_*.ini) for the given AVD folder. Files written before
    /// <paramref name="notBeforeUtc"/> belong to an emulator that was killed or crashed, and are ignored.
    /// </summary>
    public static EmulatorDiscovery? FindDiscovery(string runningDirectory, string avdDirectory, DateTime notBeforeUtc)
    {
        if (!Directory.Exists(runningDirectory)) return null;

        foreach (var file in Directory.GetFiles(runningDirectory, "pid_*.ini"))
        {
            Dictionary<string, string> values;
            try
            {
                if (File.GetLastWriteTimeUtc(file) < notBeforeUtc) continue;
                values = File.ReadAllLines(file)
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .GroupBy(parts => parts[0].Trim())
                    .ToDictionary(g => g.Key, g => g.First()[1].Trim());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // being written by the emulator: next poll
            }

            if (values.TryGetValue("avd.dir", out var dir)
                && string.Equals(Path.GetFullPath(dir), Path.GetFullPath(avdDirectory), StringComparison.OrdinalIgnoreCase)
                && values.TryGetValue("grpc.token", out var token)
                && values.TryGetValue("port.serial", out var serial)
                && int.TryParse(serial, out var consolePort))
            {
                return new EmulatorDiscovery(token, consolePort);
            }
        }
        return null;
    }

    public async Task<EmulatorSession> StartAsync(string avdName, CancellationToken ct)
    {
        PrepareDirectories(Runtime);
        try
        {
            return await StartOnceAsync(avdName, coldBoot: false, ct);
        }
        catch (EmulatorExitedException)
        {
            // A snapshot left "dirty" by a killed emulator (crash, power loss) crashes every snapshot boot:
            // one cold boot rewrites it.
            return await StartOnceAsync(avdName, coldBoot: true, ct);
        }
    }

    private async Task<EmulatorSession> StartOnceAsync(string avdName, bool coldBoot, CancellationToken ct)
    {
        var grpcPort = FreeTcpPort();
        // Small margin: the file system's write time can lag the clock read here.
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        // Not disposed on success: the running emulator keeps writing to these pipes for the whole session.
        var process = Process.Start(BuildStartInfo(Runtime, avdName, grpcPort, coldBoot))
            ?? throw new InvalidOperationException("Impossible de lancer l'émulateur.");
        PipeToLog(process, coldBoot ? "démarrage à froid" : "démarrage");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(BootTimeout);
        try
        {
            var running = DiscoveryDirectory;
            var avdDirectory = Path.Combine(Runtime.AvdHome, $"{avdName}.avd");
            EmulatorDiscovery? discovery;
            while ((discovery = FindDiscovery(running, avdDirectory, startedUtc)) is null)
            {
                if (process.HasExited) throw new EmulatorExitedException(process.ExitCode);
                await Task.Delay(500, deadline.Token);
            }

            var session = new EmulatorSession(process.Id, grpcPort, discovery.GrpcToken, discovery.ConsolePort);
            while (!await BootCompletedAsync(session, deadline.Token))
            {
                if (process.HasExited) throw new EmulatorExitedException(process.ExitCode);
                await Task.Delay(2000, deadline.Token);
            }
            return session;
        }
        catch (Exception e)
        {
            // Never leave a hidden emulator behind: no window, no console, 4 GB of RAM.
            KillQuietly(process);
            process.Dispose();
            if (e is OperationCanceledException && !ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Android n'a pas fini de démarrer en {BootTimeout.TotalSeconds:F0} s.");
            }
            throw;
        }
    }

    public Task StopAsync(EmulatorSession session, CancellationToken ct) =>
        Adb(session, ["emu", "kill"], ct);

    private async Task<bool> BootCompletedAsync(EmulatorSession session, CancellationToken ct)
    {
        var result = await Adb(session, ["shell", "getprop", "sys.boot_completed"], ct);
        return result.ExitCode == 0 && result.StandardOutput.Trim() == "1";
    }

    private Task<ProcessResult> Adb(EmulatorSession session, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var adb = Path.Combine(Runtime.Sdk, "platform-tools", "adb.exe");
        return runner.RunAsync(adb, ["-s", session.AdbSerial, .. arguments], Path.GetDirectoryName(adb)!, TimeSpan.FromSeconds(30), ct, AndroidEnvironment(Runtime));
    }

    private void PipeToLog(Process process, string header)
    {
        var log = new EmulatorLog(Path.Combine(paths.Logs, "emulator.log"), header);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) log.StreamEnded();
            else log.Write(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) log.StreamEnded();
            else log.Write(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class EmulatorExitedException(int exitCode)
        : InvalidOperationException($"L'émulateur s'est arrêté au démarrage (code 0x{exitCode:X}). Détails dans logs\\emulator.log.");
}
