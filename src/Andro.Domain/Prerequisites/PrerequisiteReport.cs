namespace Andro.Domain.Prerequisites;

public enum Acceleration
{
    Usable,
    HypervisorPlatformDisabled,
    VirtualizationDisabledInBios,

    /// <summary>Emulator not installed yet, or an answer Andro does not recognise.</summary>
    Unknown,
}

public sealed record PrerequisiteReport(Acceleration Acceleration, string AccelerationDetail, long FreeDiskBytes, long TotalRamBytes)
{
    private const long GiB = 1024L * 1024 * 1024;

    public bool DiskOk => FreeDiskBytes >= 15 * GiB;
    public bool RamOk => TotalRamBytes >= 8 * GiB;
}
