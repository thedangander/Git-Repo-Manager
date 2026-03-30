using GitRepoManager.Abstractions;
using GitRepoManager.Models;

namespace GitRepoManager.UI;

/// <summary>
/// Handles all console rendering with consistent styling.
/// </summary>
public sealed class ConsoleRenderer : IRenderer
{
    private const int TableWidth = 100;
    private readonly IConsoleIO _console;

    public ConsoleRenderer(IConsoleIO console)
    {
        _console = console;
    }

    public void Clear() => _console.Clear();

    public void Write(string text) => _console.Write(text);

    public void WriteLine(string text = "") => _console.WriteLine(text);

    public void WriteHeader(string title)
    {
        _console.SetForegroundColor(ConsoleColor.Cyan);
        _console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        _console.WriteLine($"║{title.PadLeft((78 + title.Length) / 2).PadRight(78)}║");
        _console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        _console.ResetColor();
    }

    public void WriteSubtext(string text)
    {
        _console.SetForegroundColor(ConsoleColor.DarkGray);
        _console.WriteLine(text);
        _console.ResetColor();
    }

    public void WriteSeparator()
    {
        _console.SetForegroundColor(ConsoleColor.DarkGray);
        _console.WriteLine(new string('─', TableWidth));
        _console.ResetColor();
    }

    public void WriteSuccess(string message)
    {
        _console.SetForegroundColor(ConsoleColor.Green);
        _console.WriteLine(message);
        _console.ResetColor();
    }

    public void WriteWarning(string message)
    {
        _console.SetForegroundColor(ConsoleColor.Yellow);
        _console.WriteLine(message);
        _console.ResetColor();
    }

    public void WriteError(string message)
    {
        _console.SetForegroundColor(ConsoleColor.Red);
        _console.WriteLine(message);
        _console.ResetColor();
    }

    public void WriteInfo(string message)
    {
        _console.SetForegroundColor(ConsoleColor.Cyan);
        _console.WriteLine(message);
        _console.ResetColor();
    }

    public void WriteTableHeader()
    {
        _console.SetForegroundColor(ConsoleColor.DarkGray);
        _console.WriteLine($"{"#",-3} {"Repository",-25} {"Branch",-20} {"Latest Commit",-30} {"↑↓",-10} {"Size",-10}");
        _console.WriteLine(new string('─', TableWidth));
        _console.ResetColor();
    }

    public void WriteRepositoryRow(GitRepository repo, int index, bool isSelected)
    {
        if (isSelected)
        {
            _console.SetBackgroundColor(ConsoleColor.DarkBlue);
            _console.SetForegroundColor(ConsoleColor.White);
        }

        var indicator = isSelected ? "►" : " ";
        var name = Truncate(repo.Name, 24);
        var branch = Truncate(repo.CurrentBranch, 19);
        var commit = Truncate(repo.LatestCommit, 29);
        var status = repo.HasRemote ? $"↑{repo.Ahead} ↓{repo.Behind}" : "No remote";
        var size = FormatSize(repo.SizeOnDisk);
        var changeMarker = repo.HasUncommittedChanges ? "*" : "";

        _console.Write($"{indicator}{index + 1,-2} ");

        WriteWithColor($"{name + changeMarker,-25} ",
            !isSelected && repo.HasUncommittedChanges ? ConsoleColor.Yellow : null);

        WriteWithColor($"{branch,-20} ",
            isSelected ? null : ConsoleColor.Green);

        WriteWithColor($"{commit,-30} ",
            isSelected ? null : ConsoleColor.Gray);

        WriteWithColor($"{status,-10} ",
            isSelected ? null : (repo.Ahead > 0 || repo.Behind > 0 ? ConsoleColor.Magenta : ConsoleColor.DarkGray));

        WriteWithColor($"{size,-10}",
            isSelected ? null : ConsoleColor.DarkCyan);

        _console.ResetColor();
        _console.WriteLine();
    }

    private void WriteWithColor(string text, ConsoleColor? color)
    {
        if (color.HasValue)
            _console.SetForegroundColor(color.Value);
        _console.Write(text);
    }

    public void WriteMenuBar(bool hasVSCode, int secondsUntilRefresh)
    {
        WriteSeparator();

        WriteKeyHint("↑/↓", "Navigate");
        WriteKeyHint("Enter", "Select");
        WriteKeyHint("R", "Rescan");
        WriteKeyHint("F", "Fetch All");
        WriteKeyHint("Q", "Quit");
        if (hasVSCode)
            WriteKeyHint("V", "VS Code");

        _console.WriteLine();
        WriteSubtext($"\n * = has uncommitted changes | Auto-refresh in {secondsUntilRefresh}s");
    }

    private void WriteKeyHint(string key, string description)
    {
        _console.Write(" ");
        _console.SetForegroundColor(ConsoleColor.Cyan);
        _console.Write($"[{key}]");
        _console.ResetColor();
        _console.Write($" {description} ");
    }

    public void WriteMenuItem(string key, string description, ConsoleColor? keyColor = null)
    {
        _console.SetForegroundColor(keyColor ?? ConsoleColor.White);
        _console.Write($"  [{key}] ");
        _console.ResetColor();
        if (keyColor == ConsoleColor.Red)
            _console.SetForegroundColor(ConsoleColor.Red);
        _console.WriteLine(description);
        _console.ResetColor();
    }

    public void WriteRepoDetails(GitRepository repo)
    {
        _console.SetForegroundColor(ConsoleColor.Gray);
        _console.WriteLine($"  Path:    {repo.Path}");
        _console.WriteLine($"  Branch:  {repo.CurrentBranch}");
        _console.WriteLine($"  Commit:  {repo.LatestCommit}");
        _console.WriteLine($"  Size:    {FormatSize(repo.SizeOnDisk)}");

        if (repo.HasRemote)
            _console.WriteLine($"  Status:  {repo.Ahead} ahead, {repo.Behind} behind");
        else
            _console.WriteLine("  Status:  No remote tracking branch");

        _console.ResetColor();

        if (repo.HasUncommittedChanges)
            WriteWarning("  ⚠ Has uncommitted changes");
    }

    public void WaitForKey(string message = "Press any key to continue...")
    {
        _console.WriteLine($"\n{message}");
        _console.ReadKey(true);
    }

    public string? Prompt(string message)
    {
        _console.Write(message);
        return _console.ReadLine();
    }

    private static string Truncate(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLength ? str : str[..(maxLength - 2)] + "..";
    }

    public static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
