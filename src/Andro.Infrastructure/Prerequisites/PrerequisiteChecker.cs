using System.Runtime.InteropServices;
using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Domain.Prerequisites;

namespace Andro.Infrastructure.Prerequisites;

public sealed class PrerequisiteChecker(IProcessRunner runner) : IPrerequisiteChecker
{
    public async Task<PrerequisiteReport> CheckAsync(AndroPaths paths, CancellationToken ct)
    {
        var (acceleration, detail) = await CheckAccelerationAsync(paths, ct);
        return new PrerequisiteReport(acceleration, detail, FreeDiskBytes(paths.Root), TotalRamBytes());
    }

    private async Task<(Acceleration, string)> CheckAccelerationAsync(AndroPaths paths, CancellationToken ct)
    {
        var emulator = Path.Combine(paths.Sdk, "emulator", "emulator.exe");
        if (!File.Exists(emulator))
        {
            return (Acceleration.Unknown, "Émulateur pas encore installé.");
        }

        var result = await runner.RunAsync(emulator, ["-accel-check"], Path.GetDirectoryName(emulator)!, TimeSpan.FromSeconds(30), ct);
        var output = (result.StandardOutput + result.StandardError).Trim();
        return (Classify(result.ExitCode, output), output);
    }

    private static Acceleration Classify(int exitCode, string output)
    {
        if (exitCode == 0) return Acceleration.Usable;

        bool Has(string text) => output.Contains(text, StringComparison.OrdinalIgnoreCase);
        var off = Has("not installed") || Has("disabled") || Has("not enabled");

        if (Has("WHPX") && off) return Acceleration.HypervisorPlatformDisabled;
        if ((Has("VT-x") || Has("VT feature") || Has("SVM") || Has("virtualization")) && Has("disabled")) return Acceleration.VirtualizationDisabledInBios;
        return Acceleration.Unknown;
    }

    private static long FreeDiskBytes(string root) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;

    private static long TotalRamBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
