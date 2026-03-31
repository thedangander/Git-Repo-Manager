using GitRepoManager.Abstractions;
using GitRepoManager.Models;

namespace GitRepoManager.Services;

/// <summary>
/// Scans directories to discover Git repositories.
/// </summary>
public sealed class RepositoryScanner : IRepositoryScanner
{
    private readonly IGitService _git;
    private readonly IFileSystem _fileSystem;
    private readonly int _maxDepth;
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastChange = new(StringComparer.OrdinalIgnoreCase);

    public RepositoryScanner(IGitService git, IFileSystem fileSystem, int maxDepth = AppConfig.DefaultMaxScanDepth)
    {
        _git = git;
        _fileSystem = fileSystem;
        _maxDepth = maxDepth;
    }

    public event Action<string>? RepositoryFound;
    public event Action<string>? RepositoryChanged;

    public List<GitRepository> Scan(string rootPath)
    {
        var repositories = new List<GitRepository>();
        ScanDirectory(rootPath, 0, repositories);
        // Reset and start watchers for discovered repositories
        StartWatchers(repositories);
        return repositories;
    }

    private void StartWatchers(List<GitRepository> repositories)
    {
        // Dispose old watchers
        foreach (var w in _watchers.Values) try { w.Dispose(); } catch { }
        _watchers.Clear();
        _lastChange.Clear();

        foreach (var repo in repositories)
        {
            try
            {
                var gitDir = Path.Combine(repo.Path, ".git");
                if (!_fileSystem.DirectoryExists(gitDir))
                    continue;

                var watcher = new FileSystemWatcher(gitDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };

                FileSystemEventHandler handler = (s, e) => OnRepoChanged(repo.Path);
                RenamedEventHandler rhandler = (s, e) => OnRepoChanged(repo.Path);

                watcher.Changed += handler;
                watcher.Created += handler;
                watcher.Deleted += handler;
                watcher.Renamed += rhandler;
                watcher.EnableRaisingEvents = true;

                _watchers[repo.Path] = watcher;
            }
            catch
            {
                // ignore watchers we can't create
            }
        }
    }

    private void OnRepoChanged(string repoPath)
    {
        // Throttle frequent events for same repo
        lock (_lastChange)
        {
            var now = DateTime.UtcNow;
            if (_lastChange.TryGetValue(repoPath, out var last) && (now - last).TotalMilliseconds < 800) return;
            _lastChange[repoPath] = now;
        }

        RepositoryChanged?.Invoke(repoPath);
    }

    private void ScanDirectory(string path, int depth, List<GitRepository> repositories)
    {
        if (depth > _maxDepth) return;

        try
        {
            if (_fileSystem.DirectoryExists(Path.Combine(path, ".git")))
            {
                var repo = CreateRepository(path);
                if (repo != null)
                {
                    repositories.Add(repo);
                    RepositoryFound?.Invoke(repo.Name);
                }
                return; // Don't scan inside git repos
            }

            foreach (var dir in _fileSystem.GetDirectories(path))
            {
                var dirName = Path.GetFileName(dir);
                if (ShouldSkipDirectory(dirName))
                    continue;

                ScanDirectory(dir, depth + 1, repositories);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }
    }

    private static bool ShouldSkipDirectory(string name)
        => name.StartsWith('.') || AppConfig.IgnoredDirectories.Contains(name);

    public GitRepository? CreateRepository(string repoPath)
    {
        try
        {
            var branch = _git.GetCurrentBranch(repoPath);
            var aheadBehind = _git.GetAheadBehind(repoPath, branch);

            return new GitRepository
            {
                Path = repoPath,
                Name = Path.GetFileName(repoPath),
                CurrentBranch = branch,
                LatestCommit = _git.GetLatestCommit(repoPath),
                Ahead = aheadBehind?.ahead ?? 0,
                Behind = aheadBehind?.behind ?? 0,
                HasRemote = aheadBehind.HasValue,
                SizeOnDisk = CalculateDirectorySize(repoPath),
                Branches = _git.GetBranches(repoPath),
                HasUncommittedChanges = _git.HasUncommittedChanges(repoPath)
            };
        }
        catch
        {
            return null;
        }
    }

    private long CalculateDirectorySize(string path)
    {
        try
        {
            return _fileSystem.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => _fileSystem.GetFileLength(f));
        }
        catch
        {
            return 0;
        }
    }
}
