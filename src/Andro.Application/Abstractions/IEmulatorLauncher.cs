namespace Andro.Application.Abstractions;

/// <summary>A running headless emulator: gRPC on <see cref="GrpcPort"/> with <see cref="GrpcToken"/>, adb on emulator-<see cref="ConsolePort"/>.</summary>
public sealed record EmulatorSession(int ProcessId, int GrpcPort, string GrpcToken, int ConsolePort)
{
    public string AdbSerial => $"emulator-{ConsolePort}";
}

public interface IEmulatorLauncher
{
    /// <summary>Starts the AVD and returns once Android reports boot completed.</summary>
    Task<EmulatorSession> StartAsync(string avdName, CancellationToken ct);

    Task StopAsync(EmulatorSession session, CancellationToken ct);
}
