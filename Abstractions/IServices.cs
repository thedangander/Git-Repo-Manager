using GitRepoManager.Models;

namespace GitRepoManager.Abstractions;

/// <summary>
/// Git operations abstraction.
/// </summary>
public interface IGitService
{
    string RunCommand(string workingDir, string arguments);
    string GetCurrentBranch(string repoPath);
    string GetLatestCommit(string repoPath);
    (int ahead, int behind)? GetAheadBehind(string repoPath, string branch);
    IReadOnlyList<string> GetBranches(string repoPath);
    bool HasUncommittedChanges(string repoPath);
    void Fetch(string repoPath, bool prune = true);
    string Pull(string repoPath);
    string Checkout(string repoPath, string branch, bool createTracking = false);
}

/// <summary>
/// Repository scanning abstraction.
/// </summary>
public interface IRepositoryScanner
{
    event Action<string>? RepositoryFound;
        event Action<string>? RepositoryChanged;
    List<GitRepository> Scan(string rootPath);
    GitRepository? CreateRepository(string repoPath);
}

/// <summary>
/// External application launching abstraction.
/// </summary>
public interface IExternalAppService
{
    bool IsVSCodeInstalled { get; }
    void OpenInVSCode(string path);
    bool OpenInTerminal(string path);
}

/// <summary>
/// UI rendering abstraction.
/// </summary>
public interface IRenderer
{
    void Clear();
    void WriteHeader(string title);
    void WriteSubtext(string text);
    void WriteSeparator();
    void WriteSuccess(string message);
    void WriteWarning(string message);
    void WriteError(string message);
    void WriteInfo(string message);
    void WriteTableHeader();
    void WriteRepositoryRow(GitRepository repo, int index, bool isSelected);
    void WriteMenuBar(bool hasVSCode, int secondsUntilRefresh);
    void WriteMenuItem(string key, string description, ConsoleColor? keyColor = null);
    void WriteRepoDetails(GitRepository repo);
    void WaitForKey(string message = "Press any key to continue...");
    string? Prompt(string message);
    void WriteLine(string text = "");
    void Write(string text);
}

/// <summary>
/// Abstracts time-related operations for testability.
/// </summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
