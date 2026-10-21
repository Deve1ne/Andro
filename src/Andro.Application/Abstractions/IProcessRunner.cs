namespace Andro.Application.Abstractions;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    /// <param name="environment">Variables added to the child's environment (adb needs the Android homes).</param>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? environment = null);
}
