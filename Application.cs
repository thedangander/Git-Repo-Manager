using System.Diagnostics;
using GitRepoManager.Abstractions;
using GitRepoManager.Models;

namespace GitRepoManager;

/// <summary>
/// Main application coordinator handling the interactive repository management experience.
/// </summary>
public sealed class Application
{
    private readonly AppConfig _config;
    private readonly IGitService _git;
    private readonly IRepositoryScanner _scanner;
    private readonly IExternalAppService _externalApps;
    private readonly IRenderer _ui;
    private readonly IClock _clock;
    private readonly IFileSystem _fileSystem;
    private readonly IConsoleIO _console;

    private List<GitRepository> _repositories = [];
    private int _selectedIndex;
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _isRefreshing;

    public Application(
        AppConfig config,
        IGitService git,
        IRepositoryScanner scanner,
        IExternalAppService externalApps,
        IRenderer ui,
        IClock clock,
        IFileSystem fileSystem,
        IConsoleIO console)
    {
        _config = config;
        _git = git;
        _scanner = scanner;
        _externalApps = externalApps;
        _ui = ui;
        _clock = clock;
        _fileSystem = fileSystem;
        _console = console;
    }

    public void Run()
    {
        ScanForRepositories();
        RunInteractiveLoop();
        _ui.Clear();
        _console.WriteLine("Goodbye!");
    }

    private void ScanForRepositories()
    {
        _ui.Clear();
        _ui.WriteInfo("🔍 Scanning for Git repositories...");

        _scanner.RepositoryFound += name => _console.WriteLine($"  Found: {name}");
        _repositories = _scanner.Scan(_config.ScanPath);
        _selectedIndex = 0;

        _console.WriteLine($"Found {_repositories.Count} repositories.\n");
    }

    private void RunInteractiveLoop()
    {
        _lastRefresh = _clock.Now;

        while (true)
        {
            DisplayRepositoryList();

            var key = WaitForKeyWithTimeout(AppConfig.KeyTimeoutMs);

            if (key.HasValue)
            {
                if (!HandleKeyPress(key.Value))
                    return; // Exit requested

                _lastRefresh = _clock.Now;
            }
            else if (ShouldAutoRefresh())
            {
                RefreshAllRepositories();
                _lastRefresh = _clock.Now;
            }
        }
    }

    private bool HandleKeyPress(ConsoleKey key) => key switch
    {
        ConsoleKey.UpArrow => NavigateUp(),
        ConsoleKey.DownArrow => NavigateDown(),
        ConsoleKey.Enter when _repositories.Count > 0 => ShowRepositoryActions(),
        ConsoleKey.R => Rescan(),
        ConsoleKey.F => FetchAll(),
        ConsoleKey.V when _externalApps.IsVSCodeInstalled && _repositories.Count > 0 => OpenSelectedInVSCode(),
        ConsoleKey.Q => false,
        _ => true
    };

    private bool NavigateUp()
    {
        if (_selectedIndex > 0) _selectedIndex--;
        return true;
    }

    private bool NavigateDown()
    {
        if (_selectedIndex < _repositories.Count - 1) _selectedIndex++;
        return true;
    }

    private bool Rescan()
    {
        ScanForRepositories();
        return true;
    }

    private bool FetchAll()
    {
        _ui.Clear();
        _ui.WriteInfo("Fetching all repositories...\n");

        foreach (var repo in _repositories)
        {
            _console.Write($"  Fetching {repo.Name}... ");
            _git.Fetch(repo.Path);
            _ui.WriteSuccess("✓");
        }

        _ui.WriteSuccess("\n✓ All repositories fetched!");
        RefreshAllRepositories();
        _ui.WaitForKey();
        return true;
    }

    private bool OpenSelectedInVSCode()
    {
        try
        {
            _externalApps.OpenInVSCode(_repositories[_selectedIndex].Path);
            _ui.WriteSuccess("\n✓ Opened in VS Code");
        }
        catch (Exception ex)
        {
            _ui.WriteError($"\nFailed to open VS Code: {ex.Message}");
        }
        _ui.WaitForKey();
        return true;
    }

    private void DisplayRepositoryList()
    {
        _ui.Clear();
        _ui.WriteHeader("GIT REPOSITORY MANAGER");
        _ui.WriteSubtext($"Scanning: {_config.ScanPath}\n");

        if (_repositories.Count == 0)
        {
            _ui.WriteWarning("No Git repositories found in this directory.");
            return;
        }

        _ui.WriteTableHeader();

        for (var i = 0; i < _repositories.Count; i++)
        {
            _ui.WriteRepositoryRow(_repositories[i], i, i == _selectedIndex);
        }

        _console.WriteLine();

        var secondsUntilRefresh = Math.Max(0,
            _config.RefreshIntervalSeconds - (int)(_clock.Now - _lastRefresh).TotalSeconds);
        _ui.WriteMenuBar(_externalApps.IsVSCodeInstalled, secondsUntilRefresh);
    }

    private bool ShowRepositoryActions()
    {
        if (_repositories.Count == 0 || _selectedIndex >= _repositories.Count)
            return true;

        var repo = _repositories[_selectedIndex];

        while (true)
        {
            DisplayRepositoryActionMenu(repo);

            var key = _console.ReadKey(true).Key;
            var result = HandleRepositoryAction(key, repo);

            if (result == ActionResult.Back)
                return true;
            if (result == ActionResult.Deleted)
                return true;

            RefreshRepository(repo);
        }
    }

    private void DisplayRepositoryActionMenu(GitRepository repo)
    {
        _ui.Clear();
        _ui.WriteHeader($"Repository: {repo.Name}");
        _console.WriteLine();
        _ui.WriteRepoDetails(repo);
        _console.WriteLine();
        _ui.WriteSeparator();
        _console.WriteLine("\n  Select an action:\n");

        _ui.WriteMenuItem("1", "Sync (fetch + pull)");
        _ui.WriteMenuItem("2", "Switch branch");
        _ui.WriteMenuItem("3", "Fetch only");
        _ui.WriteMenuItem("4", "Open in terminal");

        if (_externalApps.IsVSCodeInstalled)
            _ui.WriteMenuItem("5", "Open in VS Code");

        _ui.WriteMenuItem("6", "Delete local repository", ConsoleColor.Red);
        _console.WriteLine();
        _ui.WriteMenuItem("Esc/B", "Back to list");
    }

    private enum ActionResult { Continue, Back, Deleted }

    private ActionResult HandleRepositoryAction(ConsoleKey key, GitRepository repo) => key switch
    {
        ConsoleKey.D1 or ConsoleKey.NumPad1 => SyncRepository(repo),
        ConsoleKey.D2 or ConsoleKey.NumPad2 => SwitchBranch(repo),
        ConsoleKey.D3 or ConsoleKey.NumPad3 => FetchRepository(repo),
        ConsoleKey.D4 or ConsoleKey.NumPad4 => OpenInTerminal(repo),
        ConsoleKey.D5 or ConsoleKey.NumPad5 when _externalApps.IsVSCodeInstalled => OpenInVSCode(repo),
        ConsoleKey.D6 or ConsoleKey.NumPad6 => DeleteRepository(repo) ? ActionResult.Deleted : ActionResult.Continue,
        ConsoleKey.Escape or ConsoleKey.B => ActionResult.Back,
        _ => ActionResult.Continue
    };

    private ActionResult SyncRepository(GitRepository repo)
    {
        _ui.Clear();
        _ui.WriteInfo($"Syncing {repo.Name}...\n");

        _console.WriteLine("Fetching...");
        _git.Fetch(repo.Path);
        _console.WriteLine("Fetch complete.");

        _console.WriteLine("\nPulling...");
        var result = _git.Pull(repo.Path);
        _console.WriteLine(string.IsNullOrEmpty(result) ? "Already up to date." : result);

        _ui.WriteSuccess("\n✓ Sync complete!");
        _ui.WaitForKey();
        return ActionResult.Continue;
    }

    private ActionResult FetchRepository(GitRepository repo)
    {
        _ui.Clear();
        _ui.WriteInfo($"Fetching {repo.Name}...\n");
        _git.Fetch(repo.Path);
        _ui.WriteSuccess("\n✓ Fetch complete!");
        _ui.WaitForKey();
        return ActionResult.Continue;
    }

    private ActionResult SwitchBranch(GitRepository repo)
    {
        _ui.Clear();
        _ui.WriteInfo($"Switch branch for {repo.Name}\n");

        var localBranches = repo.Branches
            .Where(b => !b.StartsWith("remotes/"))
            .ToList();

        var remoteBranches = repo.Branches
            .Where(b => b.StartsWith("remotes/") && !b.Contains("HEAD"))
            .Select(b => b.Replace("remotes/origin/", "").Replace("remotes/", ""))
            .Distinct()
            .Where(b => !localBranches.Contains(b))
            .ToList();

        _console.WriteLine("  Local branches:");
        for (var i = 0; i < localBranches.Count; i++)
        {
            var current = localBranches[i] == repo.CurrentBranch ? " (current)" : "";
            _console.WriteLine($"    {i + 1}. {localBranches[i]}{current}");
        }

        if (remoteBranches.Count > 0)
        {
            _ui.WriteSubtext("\n  Remote branches (will create local tracking branch):");
            for (var i = 0; i < remoteBranches.Count; i++)
            {
                _console.WriteLine($"    {localBranches.Count + i + 1}. {remoteBranches[i]}");
            }
        }

        _console.WriteLine();
        var input = _ui.Prompt("Enter branch number (or 0 to cancel): ");

        if (int.TryParse(input, out var choice) && choice > 0)
        {
            string branchName;
            bool isRemote;

            if (choice <= localBranches.Count)
            {
                branchName = localBranches[choice - 1];
                isRemote = false;
            }
            else if (choice <= localBranches.Count + remoteBranches.Count)
            {
                branchName = remoteBranches[choice - localBranches.Count - 1];
                isRemote = true;
            }
            else
            {
                _ui.WriteError("Invalid selection.");
                _ui.WaitForKey();
                return ActionResult.Continue;
            }

            _console.Write($"\nSwitching to {branchName}... ");
            _git.Checkout(repo.Path, branchName, isRemote);
            _ui.WriteSuccess("✓");
        }

        _ui.WaitForKey();
        return ActionResult.Continue;
    }

    private ActionResult OpenInTerminal(GitRepository repo)
    {
        try
        {
            if (_externalApps.OpenInTerminal(repo.Path))
                _ui.WriteSuccess("\n✓ Opened in Terminal");
            else
                _ui.WriteError("\nNo supported terminal emulator found.");
        }
        catch (Exception ex)
        {
            _ui.WriteError($"\nFailed to open terminal: {ex.Message}");
        }
        _ui.WaitForKey();
        return ActionResult.Continue;
    }

    private ActionResult OpenInVSCode(GitRepository repo)
    {
        try
        {
            _externalApps.OpenInVSCode(repo.Path);
            _ui.WriteSuccess("\n✓ Opened in VS Code");
        }
        catch (Exception ex)
        {
            _ui.WriteError($"\nFailed to open VS Code: {ex.Message}");
        }
        _ui.WaitForKey();
        return ActionResult.Continue;
    }

    private bool DeleteRepository(GitRepository repo)
    {
        _ui.Clear();
        _ui.WriteError("╔══════════════════════════════════════════════════════════════════════════════╗");
        _ui.WriteError("║                              ⚠ WARNING ⚠                                     ║");
        _ui.WriteError("╚══════════════════════════════════════════════════════════════════════════════╝");

        _console.WriteLine("\n  You are about to DELETE the following repository:\n");
        _ui.WriteWarning($"  {repo.Path}");
        _console.WriteLine($"\n  Size: {FormatSize(repo.SizeOnDisk)}");

        if (repo.HasUncommittedChanges)
            _ui.WriteError("\n  ⚠ This repository has UNCOMMITTED CHANGES that will be LOST!");

        if (repo.Ahead > 0)
            _ui.WriteError($"\n  ⚠ This repository has {repo.Ahead} UNPUSHED COMMITS that will be LOST!");

        _ui.WriteError("\n  THIS ACTION CANNOT BE UNDONE!");

        var confirmation = _ui.Prompt("\n  Type the repository name to confirm deletion: ");

        if (confirmation != repo.Name)
        {
            _ui.WriteWarning("\n  Deletion cancelled.");
            _ui.WaitForKey();
            return false;
        }

        _console.Write("\n  Deleting...");

        try
        {
            _fileSystem.DeleteDirectoryRecursive(repo.Path);
            _ui.WriteSuccess(" ✓ Deleted!");

            _repositories.Remove(repo);
            if (_selectedIndex >= _repositories.Count && _selectedIndex > 0)
                _selectedIndex--;

            _ui.WaitForKey();
            return true;
        }
        catch (Exception ex)
        {
            _ui.WriteError($" Failed: {ex.Message}");
            _ui.WaitForKey();
            return false;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private void RefreshRepository(GitRepository repo)
    {
        var index = _repositories.IndexOf(repo);
        if (index >= 0)
        {
            var updated = _scanner.CreateRepository(repo.Path);
            if (updated != null)
                _repositories[index] = updated;
        }
    }

    private void RefreshAllRepositories()
    {
        if (_isRefreshing) return;

        _isRefreshing = true;
        try
        {
            for (var i = 0; i < _repositories.Count; i++)
            {
                var updated = _scanner.CreateRepository(_repositories[i].Path);
                if (updated != null)
                    _repositories[i] = updated;
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private bool ShouldAutoRefresh()
        => !_isRefreshing && (_clock.Now - _lastRefresh).TotalSeconds >= _config.RefreshIntervalSeconds;

    private ConsoleKey? WaitForKeyWithTimeout(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (_console.KeyAvailable)
                return _console.ReadKey(true).Key;

            Thread.Sleep(AppConfig.KeyPollIntervalMs);
        }
        return null;
    }
}
