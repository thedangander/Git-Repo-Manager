namespace GitRepoManager.Abstractions;

/// <summary>
/// Abstracts console I/O operations for testability.
/// </summary>
public interface IConsoleIO
{
    void Clear();
    void Write(string text);
    void WriteLine(string text = "");
    void SetForegroundColor(ConsoleColor color);
    void SetBackgroundColor(ConsoleColor color);
    void ResetColor();
    string? ReadLine();
    ConsoleKeyInfo ReadKey(bool intercept = false);
    bool KeyAvailable { get; }
}

/// <summary>
/// Default implementation using System.Console.
/// </summary>
public sealed class SystemConsoleIO : IConsoleIO
{
    public void Clear() => Console.Clear();
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text = "") => Console.WriteLine(text);
    public void SetForegroundColor(ConsoleColor color) => Console.ForegroundColor = color;
    public void SetBackgroundColor(ConsoleColor color) => Console.BackgroundColor = color;
    public void ResetColor() => Console.ResetColor();
    public string? ReadLine() => Console.ReadLine();
    public ConsoleKeyInfo ReadKey(bool intercept = false) => Console.ReadKey(intercept);
    public bool KeyAvailable => Console.KeyAvailable;
}
