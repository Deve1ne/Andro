using Andro.Infrastructure.Emulator;

namespace Andro.Infrastructure.Tests.Emulator;

public sealed class EmulatorLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("andro-log-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File => Path.Combine(_dir, "emulator.log");

    [Fact]
    public void Lines_after_both_streams_ended_are_dropped_without_throwing()
    {
        var log = new EmulatorLog(File, "start");
        log.Write("before");
        log.StreamEnded();
        log.Write("between");
        log.StreamEnded();

        log.Write("late line from a reader thread");
        log.Dispose();

        var text = System.IO.File.ReadAllText(File);
        Assert.Contains("before", text);
        Assert.Contains("between", text);
        Assert.DoesNotContain("late line", text);
    }

    [Fact]
    public void Token_lines_are_never_written()
    {
        using (var log = new EmulatorLog(File, "start"))
        {
            log.Write("grpc.token=secret");
            log.Write("Your emulator is out of date");
        }

        Assert.DoesNotContain("secret", System.IO.File.ReadAllText(File));
    }

    [Fact]
    public void A_retry_appends_instead_of_erasing_the_first_crash()
    {
        var first = new EmulatorLog(File, "start (snapshot)");
        first.Write("crash details");
        // The retry opens the log while the first session's readers may still be draining.
        using (var second = new EmulatorLog(File, "start (cold boot)"))
        {
            second.Write("cold boot");
        }
        first.Dispose();

        var text = System.IO.File.ReadAllText(File);
        Assert.Contains("crash details", text);
        Assert.Contains("cold boot", text);
    }
}
