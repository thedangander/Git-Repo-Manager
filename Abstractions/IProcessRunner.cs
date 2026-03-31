namespace GitRepoManager.Abstractions;

/// <summary>
/// Abstracts process execution for testability.
/// </summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, string arguments, string? workingDirectory = null, int timeoutMs = 5000);
}

public readonly record struct ProcessResult(string Output, int ExitCode, bool TimedOut);

/// <summary>
/// Default implementation using System.Diagnostics.Process.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, string arguments, string? workingDirectory = null, int timeoutMs = 5000)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? "",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            var exited = process.WaitForExit(timeoutMs);

            var combined = string.IsNullOrEmpty(error) ? output : output + "\n" + error;
            return new ProcessResult(combined, exited ? process.ExitCode : -1, !exited);
        }
        catch
        {
            return new ProcessResult("", -1, false);
        }
    }
}
