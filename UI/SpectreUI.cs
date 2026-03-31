using GitRepoManager.Abstractions;
using GitRepoManager.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GitRepoManager.UI;

public sealed class SpectreUI
{
    private readonly AppConfig _config;
    private readonly IGitService _git;
    private readonly IRepositoryScanner _scanner;
    private readonly IExternalAppService _externalApps;
    private readonly IFileSystem _fileSystem;

    private List<GitRepository> _repositories = new();
    private int _selectedIndex;
    private int _scrollTop;
    
    private bool _isScanning;
    private int _scanFoundCount;
    private bool _showDetails;
    private string? _transientMessage;
    private Color _transientColor = Color.White;
    private DateTime _transientExpires = DateTime.MinValue;
    private bool _modalActive;
    private string _modalTitle = "";
    private List<string> _modalLines = new();
    private int _modalOffset;
    private Action? _pendingInteractive;
    
    private bool _running = true;
    private readonly object _lock = new();

    public SpectreUI(
        AppConfig config,
        IGitService git,
        IRepositoryScanner scanner,
        IExternalAppService externalApps,
        IFileSystem fileSystem)
    {
        _config = config;
        _git = git;
        _scanner = scanner;
        _externalApps = externalApps;
        _fileSystem = fileSystem;
        _scanner.RepositoryChanged += path =>
        {
            // Refresh the repository entry when filesystem changes are detected
            lock (_lock)
            {
                var idx = _repositories.FindIndex(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    var updated = _scanner.CreateRepository(path);
                    if (updated != null) _repositories[idx] = updated;
                }
            }
        };
    }

    public void Run()
    {
        Console.CursorVisible = false;
        Console.Clear();

        ScanForRepositories();
        // Start background fetch loop to keep sync state up-to-date
        _ = Task.Run(BackgroundFetchLoop);

        var lastW = Console.WindowWidth;
        var lastH = Console.WindowHeight;

        // Manual render loop (avoid nested dynamic displays). Render layout each tick and run interactive actions directly.
        while (_running)
        {
            var w = Console.WindowWidth;
            var h = Console.WindowHeight;
            if (w != lastW || h != lastH)
            {
                lastW = w; lastH = h; Console.Clear();
            }

            lock (_lock)
            {
                Console.Clear();
                    var layout = BuildLayout();
                AnsiConsole.Write(layout);
                    if (_modalActive)
                    {
                        var modalHeight = Math.Max(6, Console.WindowHeight / 2);
                        var modalWidth = Math.Max(40, Console.WindowWidth * 2 / 3);
                        var modal = SpectrePanels.BuildModal(_modalTitle, _modalLines, _modalOffset, modalHeight - 2, modalWidth);
                        AnsiConsole.Write(new Padder(modal, new Padding(1, 1)));
                    }
            }

            if (_pendingInteractive != null)
            {
                var action = _pendingInteractive;
                _pendingInteractive = null;
                action?.Invoke();
            }

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (_modalActive) HandleModalKey(key);
                else { HandleKeyPress(key); EnsureScrollPosition(); }
            }
            else Thread.Sleep(50);
        }

        Console.CursorVisible = true;
        Console.Clear();
    }

    private async Task RefreshTimerLoop()
    {
        while (_running)
        {
            await Task.Delay(1000);
                // timer removed — repository updates are driven by filesystem watchers and manual rescan
        }
    }

    private Layout BuildLayout()
    {
        // spinner removed

        // Build main layout; optionally show details panel (max ~30%)
        var mainLayout = _showDetails
            ? new Layout("Main").Ratio(1).SplitColumns(new Layout("Repositories").Ratio(70), new Layout("Details").Ratio(30))
            : new Layout("Main").Ratio(1).SplitColumns(new Layout("Repositories").Ratio(100));

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(4),
                mainLayout,
                new Layout("StatusBar").Size(3)
            );

        var repoPanelHeight = Math.Max(4, Console.WindowHeight - 4 - 3 - 4);

        string? message = null; Color msgColor = Color.White;
        lock (_lock) { if (!string.IsNullOrEmpty(_transientMessage) && DateTime.UtcNow < _transientExpires) { message = _transientMessage; msgColor = _transientColor; } }

        layout["Header"].Update(SpectrePanels.BuildHeader(_repositories.Count, _isScanning, _scanFoundCount));
        layout["Repositories"].Update(SpectrePanels.BuildRepositoryPanel(_repositories, _selectedIndex, _scrollTop, repoPanelHeight));
        if (_showDetails) layout["Details"].Update(SpectrePanels.BuildDetailsPanel(_repositories, _selectedIndex));
        layout["StatusBar"].Update(SpectrePanels.BuildStatusBar(_externalApps.IsVSCodeInstalled, _repositories.Count, message, msgColor));

        return layout;
    }

    private void HandleKeyPress(ConsoleKeyInfo key)
    {
        lock (_lock)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    if (_selectedIndex > 0) _selectedIndex--; break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    if (_selectedIndex < _repositories.Count - 1) _selectedIndex++; break;
                case ConsoleKey.Enter:
                    _pendingInteractive = SyncSelected; break;
                case ConsoleKey.D:
                    _pendingInteractive = DeleteSelected; break;
                case ConsoleKey.B:
                    _pendingInteractive = SwitchBranch; break;
                case ConsoleKey.T:
                    OpenInTerminal(); break;
                case ConsoleKey.V:
                    if (_externalApps.IsVSCodeInstalled) OpenInVSCode(); break;
                case ConsoleKey.R:
                    ScanForRepositories(); break;
                case ConsoleKey.F:
                    _pendingInteractive = FetchAll; break;
                case ConsoleKey.I:
                    _showDetails = !_showDetails; break;
                case ConsoleKey.PageUp:
                    _scrollTop = Math.Max(0, _scrollTop - 8); break;
                case ConsoleKey.PageDown:
                    _scrollTop = Math.Min(Math.Max(0, _repositories.Count - 1), _scrollTop + 8); break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    _running = false; break;
            }
        }
    }

    private void HandleModalKey(ConsoleKeyInfo key)
    {
        lock (_lock)
        {
            var height = Math.Max(6, Console.WindowHeight / 2);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    _modalOffset = Math.Max(0, _modalOffset - 1); break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    _modalOffset = Math.Min(Math.Max(0, _modalLines.Count - height), _modalOffset + 1); break;
                case ConsoleKey.PageUp:
                    _modalOffset = Math.Max(0, _modalOffset - height); break;
                case ConsoleKey.PageDown:
                    _modalOffset = Math.Min(Math.Max(0, _modalLines.Count - height), _modalOffset + height); break;
                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    _modalActive = false; break;
            }
        }
    }

    private void ScanForRepositories()
    {
        // Run scan in background and update ephemeral scan counters used by the live renderer.
        _isScanning = true;
        _scanFoundCount = 0;
        void OnFound(string _) { Interlocked.Increment(ref _scanFoundCount); }

        _scanner.RepositoryFound += OnFound;
        Task.Run(() =>
        {
            try
            {
                var repos = _scanner.Scan(_config.ScanPath);
                lock (_lock)
                {
                    _repositories = repos;
                    _selectedIndex = 0;
                    _scrollTop = 0;
                }
            }
            finally
            {
                _scanner.RepositoryFound -= OnFound;
                _isScanning = false;
            }
        });
    }

    private void EnsureScrollPosition()
    {
        if (_selectedIndex < _scrollTop) _scrollTop = _selectedIndex;
        var visibleRows = Math.Max(4, Console.WindowHeight - 4 - 3 - 4);
        if (_selectedIndex >= _scrollTop + visibleRows) _scrollTop = _selectedIndex - visibleRows + 1;
        if (_scrollTop < 0) _scrollTop = 0;
    }

    private void RefreshAllRepositories()
    {
        for (var i = 0; i < _repositories.Count; i++)
        {
            var updated = _scanner.CreateRepository(_repositories[i].Path);
            if (updated != null) _repositories[i] = updated;
        }
    }

    private GitRepository? GetSelectedRepo()
    {
        if (_repositories.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _repositories.Count) return null;
        return _repositories[_selectedIndex];
    }

    private void SyncSelected()
    {
        var repo = GetSelectedRepo(); if (repo == null) return;
        try
        {
            // Fetch, then push if we're ahead, pull if we're behind — show progress for each step
            AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(true)
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"Syncing {repo.Name}", maxValue: 3);
                    // fetch
                    _git.Fetch(repo.Path);
                    task.Increment(1);

                    // re-evaluate ahead/behind after fetch
                    var ab = _git.GetAheadBehind(repo.Path, repo.CurrentBranch);
                    if (ab.HasValue && ab.Value.ahead > 0)
                    {
                        _git.Push(repo.Path);
                        task.Increment(1);
                    }

                    if (ab.HasValue && ab.Value.behind > 0)
                    {
                        _git.Pull(repo.Path);
                        task.Increment(1);
                    }
                });

            RefreshRepository(repo);
            ShowMessage("Sync complete!", Color.Green);
        }
        catch (Exception ex)
        {
            OpenModal("Sync failed", ex.Message);
        }
    }

    // spinner removed

    private void FetchAll()
    {
        ShowMessage("Fetching all repositories...", Color.Yellow);
        var failed = new List<string>();

        // Use Spectre.Console progress bar to show per-repository progress
        AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(true)
            .HideCompleted(false)
            .Start(ctx =>
            {
                var snapshot = _repositories.ToList();
                var task = ctx.AddTask("Fetching repositories", maxValue: Math.Max(1, snapshot.Count));
                foreach (var repo in snapshot)
                {
                    try { _git.Fetch(repo.Path); } catch { failed.Add(repo.Name); }
                    task.Increment(1);
                }
            });

        RefreshAllRepositories();
        if (failed.Count > 0) ShowMessage($"Failed: {string.Join(", ", failed)}", Color.Red); else ShowMessage("All repositories fetched!", Color.Green);
    }

    private void SwitchBranch()
    {
        var repo = GetSelectedRepo(); if (repo == null) return;
        var branches = repo.Branches.Where(b => !b.StartsWith("remotes/")).ToList();
        if (branches.Count == 0) { ShowMessage("No branches available", Color.Yellow); return; }
        Console.CursorVisible = true;
        AnsiConsole.Clear();
        var branch = AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"[cyan]Switch branch for {repo.Name}[/]").PageSize(15).HighlightStyle(new Style(Color.Cyan1)).AddChoices(branches));
        Console.CursorVisible = false;
        if (branch != repo.CurrentBranch)
        {
            try
            {
                _git.Checkout(repo.Path, branch, false);
                RefreshRepository(repo);
                ShowMessage($"Switched to {branch}", Color.Green);
            }
            catch (Exception ex)
            {
                OpenModal("Checkout failed", ex.Message);
            }
        }
    }

    private void OpenInTerminal() { var repo = GetSelectedRepo(); if (repo == null) return; try { _externalApps.OpenInTerminal(repo.Path); } catch (Exception ex) { ShowMessage($"Failed: {ex.Message}", Color.Red); } }
    private void OpenInVSCode() { var repo = GetSelectedRepo(); if (repo == null) return; try { _externalApps.OpenInVSCode(repo.Path); } catch (Exception ex) { ShowMessage($"Failed: {ex.Message}", Color.Red); } }

    private void DeleteSelected()
    {
        var repo = GetSelectedRepo(); if (repo == null) return;
        Console.CursorVisible = true;
        AnsiConsole.Clear();
        var warnings = new List<string>(); if (repo.HasUncommittedChanges) warnings.Add("[red]Has UNCOMMITTED CHANGES[/]"); if (repo.Ahead > 0) warnings.Add($"[yellow]Has {repo.Ahead} UNPUSHED COMMITS[/]");
        AnsiConsole.MarkupLine($"[bold]Delete repository:[/] {Markup.Escape(repo.Path)}"); AnsiConsole.MarkupLine($"[bold]Size:[/] {FormatSize(repo.SizeOnDisk)}"); foreach (var w in warnings) AnsiConsole.MarkupLine(w);
        AnsiConsole.MarkupLine(""); AnsiConsole.MarkupLine("[bold red]This action cannot be undone![/]"); AnsiConsole.MarkupLine("");
        if (!AnsiConsole.Confirm("Delete this repository?", false)) { Console.CursorVisible = false; return; }
        var typedName = AnsiConsole.Ask<string>($"Type '[cyan]{repo.Name}[/]' to confirm:"); if (typedName != repo.Name) { ShowMessage("Name doesn't match, deletion cancelled", Color.Yellow); Console.CursorVisible = false; return; }

        try
        {
            // Enumerate files first so we can provide a per-file progress bar
            var files = _fileSystem.EnumerateFiles(repo.Path, "*", SearchOption.AllDirectories).ToList();
            var dirs = Directory.GetDirectories(repo.Path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList();

            AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(true)
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"Deleting {repo.Name}", maxValue: Math.Max(1, files.Count + dirs.Count + 1));

                    // Remove files
                    foreach (var f in files)
                    {
                        try
                        {
                            _fileSystem.SetFileAttributes(f, FileAttributes.Normal);
                            _fileSystem.DeleteFile(f);
                        }
                        catch { }
                        task.Increment(1);
                    }

                    // Remove directories (deepest first)
                    foreach (var d in dirs)
                    {
                        try { _fileSystem.SetFileAttributes(d, FileAttributes.Normal); _fileSystem.DeleteDirectory(d, false); } catch { }
                        task.Increment(1);
                    }

                    // Finally remove root
                    try { _fileSystem.DeleteDirectory(repo.Path, false); } catch { }
                    task.Increment(1);
                });

            lock (_lock)
            {
                _repositories.Remove(repo);
                if (_selectedIndex >= _repositories.Count) _selectedIndex = Math.Max(0, _repositories.Count - 1);
            }

            ShowMessage("Repository deleted", Color.Green);
        }
        catch (Exception ex)
        {
            ShowMessage($"Delete failed: {ex.Message}", Color.Red);
        }

        Console.CursorVisible = false;
    }

    private void RefreshRepository(GitRepository repo)
    {
        var index = _repositories.IndexOf(repo);
        if (index >= 0)
        {
            var updated = _scanner.CreateRepository(repo.Path);
            if (updated != null) _repositories[index] = updated;
        }
    }

    private async Task BackgroundFetchLoop()
    {
        var intervalSeconds = Math.Max(30, _config.RefreshIntervalSeconds);
        while (_running)
        {
            List<GitRepository> snapshot;
            lock (_lock) { snapshot = _repositories.ToList(); }

            foreach (var repo in snapshot)
            {
                if (!_running) break;
                try
                {
                    _git.Fetch(repo.Path);
                    lock (_lock)
                    {
                        var idx = _repositories.FindIndex(r => string.Equals(r.Path, repo.Path, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            var updated = _scanner.CreateRepository(repo.Path);
                            if (updated != null) _repositories[idx] = updated;
                        }
                    }
                }
                catch
                {
                    // ignore fetch failures; UI will show stale state until next successful fetch
                }

                await Task.Delay(300);
            }

            await Task.Delay(intervalSeconds * 1000);
        }
    }

    private void ShowMessage(string message, Color color)
    {
        lock (_lock)
        {
            _transientMessage = message;
            _transientColor = color;
            _transientExpires = DateTime.UtcNow.AddSeconds(4);
        }

        // clear after expiry on background task
        _ = Task.Run(async () =>
        {
            await Task.Delay(4500);
            lock (_lock)
            {
                if (DateTime.UtcNow >= _transientExpires) _transientMessage = null;
            }
        });
    }

    private void OpenModal(string title, string content)
    {
        lock (_lock)
        {
            _modalTitle = title;
            _modalLines = content?.Split('\n').Select(l => l.TrimEnd('\r')).ToList() ?? new List<string> { "" };
            _modalOffset = 0;
            _modalActive = true;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = new[] { "B", "KB", "MB", "GB", "TB" };
        var order = 0; var size = (double)bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:0.##} {sizes[order]}";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 1) + "…";
    }
}
