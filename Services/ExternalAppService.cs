using System.Diagnostics;
using System.Runtime.InteropServices;
using GitRepoManager.Abstractions;

namespace GitRepoManager.Services;

/// <summary>
/// Handles external application launching (terminal, VS Code).
/// </summary>
public sealed class ExternalAppService : IExternalAppService
{
    private readonly IProcessRunner _processRunner;
    private readonly Lazy<bool> _isVSCodeInstalled;

    public ExternalAppService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
        _isVSCodeInstalled = new Lazy<bool>(CheckVSCodeInstalled);
    }

    public bool IsVSCodeInstalled => _isVSCodeInstalled.Value;

    public void OpenInVSCode(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "code",
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public bool OpenInTerminal(string path)
    {
        ProcessStartInfo? startInfo = CreateTerminalStartInfo(path);
        if (startInfo == null)
            return false;

        Process.Start(startInfo);
        return true;
    }

    private static ProcessStartInfo? CreateTerminalStartInfo(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k cd /d \"{path}\"",
                UseShellExecute = true
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"-a Terminal \"{path}\"",
                UseShellExecute = false
            };
        }

        // Linux
        var terminal = FindLinuxTerminal();
        if (terminal == null) return null;

        return new ProcessStartInfo
        {
            FileName = terminal,
            Arguments = GetLinuxTerminalArgs(terminal, path),
            UseShellExecute = false
        };
    }

    private static string GetLinuxTerminalArgs(string terminal, string path) => terminal switch
    {
        "gnome-terminal" => $"--working-directory=\"{path}\"",
        "konsole" => $"--workdir \"{path}\"",
        "xfce4-terminal" => $"--working-directory=\"{path}\"",
        "xterm" => $"-e \"cd '{path}' && $SHELL\"",
        "terminator" => $"--working-directory=\"{path}\"",
        _ => $"--working-directory=\"{path}\""
    };

    private static string? FindLinuxTerminal()
    {
        string[] terminals = ["gnome-terminal", "konsole", "xfce4-terminal", "terminator", "xterm"];

        foreach (var term in terminals)
        {
            if (CommandExistsStatic(term))
                return term;
        }
        return null;
    }

    private bool CheckVSCodeInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var result = _processRunner.Run("cmd.exe", "/c where code", timeoutMs: 2000);
            return result.ExitCode == 0 && !string.IsNullOrEmpty(result.Output.Trim());
        }

        var unixResult = _processRunner.Run("which", "code", timeoutMs: 2000);
        return unixResult.ExitCode == 0 && !string.IsNullOrEmpty(unixResult.Output.Trim());
    }

    private static bool CommandExistsStatic(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return !string.IsNullOrEmpty(output) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
