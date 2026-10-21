namespace Andro.Infrastructure.Emulator;

/// <summary>
/// The emulator's stdout/stderr, appended to logs\emulator.log. Closes once both streams have ended; lines that
/// arrive later are dropped, so a reader thread can never hit a disposed writer and crash the app.
/// </summary>
public sealed class EmulatorLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private int _openStreams = 2;
    private bool _closed;

    public EmulatorLog(string file, string header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Shared: a cold-boot retry opens the log while the crashed session's readers may still be draining.
        var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        Write($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} {header}");
    }

    public void Write(string? line)
    {
        // The gRPC token must not end up in a log file.
        if (line is null || line.Contains("token", StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            if (!_closed) _writer.WriteLine(line);
        }
    }

    /// <summary>Called when stdout or stderr delivers end of stream.</summary>
    public void StreamEnded()
    {
        if (Interlocked.Decrement(ref _openStreams) == 0) Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _writer.Dispose();
        }
    }
}
