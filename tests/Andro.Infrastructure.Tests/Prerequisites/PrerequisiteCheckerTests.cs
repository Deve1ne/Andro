using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Domain.Prerequisites;
using Andro.Infrastructure.Prerequisites;
using NSubstitute;

namespace Andro.Infrastructure.Tests.Prerequisites;

public sealed class PrerequisiteCheckerTests : IDisposable
{
    private readonly AndroPaths _paths = new(Directory.CreateTempSubdirectory("andro-pre-").FullName);
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    public void Dispose() => Directory.Delete(_paths.Root, recursive: true);

    private string EmulatorExe => Path.Combine(_paths.Sdk, "emulator", "emulator.exe");

    private void InstallFakeEmulator(int exitCode, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EmulatorExe)!);
        File.WriteAllText(EmulatorExe, "");
        _runner.RunAsync(EmulatorExe, Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "-accel-check" })), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, output, ""));
    }

    private Task<PrerequisiteReport> Check() => new PrerequisiteChecker(_runner).CheckAsync(_paths, default);

    [Fact]
    public async Task Exit_zero_is_usable()
    {
        InstallFakeEmulator(0, "accel:\n0\nWHPX(10.0.26200) is installed and usable.\naccel\n");

        var report = await Check();

        Assert.Equal(Acceleration.Usable, report.Acceleration);
        Assert.Contains("WHPX(10.0.26200)", report.AccelerationDetail);
    }

    [Fact]
    public async Task Whpx_not_installed_maps_to_hypervisor_platform_disabled()
    {
        InstallFakeEmulator(1, "accel:\n9\nWHPX is not installed or not enabled. Please ensure Windows Hypervisor Platform is enabled.\naccel\n");

        Assert.Equal(Acceleration.HypervisorPlatformDisabled, (await Check()).Acceleration);
    }

    [Fact]
    public async Task Vtx_disabled_maps_to_bios()
    {
        InstallFakeEmulator(1, "accel:\n3\nVT feature disabled in BIOS/UEFI\naccel\n");

        Assert.Equal(Acceleration.VirtualizationDisabledInBios, (await Check()).Acceleration);
    }

    [Fact]
    public async Task Missing_emulator_is_unknown()
    {
        var report = await Check();

        Assert.Equal(Acceleration.Unknown, report.Acceleration);
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Unrecognised_output_is_unknown_with_raw_detail()
    {
        InstallFakeEmulator(7, "something unexpected");

        var report = await Check();

        Assert.Equal(Acceleration.Unknown, report.Acceleration);
        Assert.Equal("something unexpected", report.AccelerationDetail);
    }

    [Fact]
    public async Task Reports_disk_and_ram_of_this_machine()
    {
        var report = await Check();

        Assert.True(report.FreeDiskBytes > 0);
        Assert.True(report.TotalRamBytes > 0);
    }

    [Theory]
    [InlineData(15L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024, true, true)]
    [InlineData(15L * 1024 * 1024 * 1024 - 1, 8L * 1024 * 1024 * 1024 - 1, false, false)]
    public void Thresholds_are_15_gb_disk_and_8_gb_ram(long disk, long ram, bool diskOk, bool ramOk)
    {
        var report = new PrerequisiteReport(Acceleration.Usable, "", disk, ram);

        Assert.Equal(diskOk, report.DiskOk);
        Assert.Equal(ramOk, report.RamOk);
    }
}
