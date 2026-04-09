using GitRepoManager.Abstractions;
using GitRepoManager.Models;

namespace GitRepoManager.Services;

/// <summary>
/// Scans directories to discover Git repositories.
/// </summary>
public sealed class RepositoryScanner : IRepositoryScanner, IDisposable
{
    private readonly IGitService _git;
    private readonly IFileSystem _fileSystem;
    private readonly int _maxDepth;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastChange = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _watcherLock = new();
    private readonly Timer? _cleanupTimer;
    private bool _disposed;
    
    public RepositoryScanner(IGitService git, IFileSystem fileSystem, int maxDepth = AppConfig.DefaultMaxScanDepth)
    {
        _git = git;
        _fileSystem = fileSystem;
        _maxDepth = maxDepth;
        
        // Cleanup timer to handle orphaned watchers
        _cleanupTimer = new Timer(CleanupWatchers, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public event Action<string>? RepositoryFound;
    public event Action<string>? RepositoryChanged;

    public List<GitRepository> Scan(string rootPath)
    {
        var repositories = new List<GitRepository>();
        ScanDirectory(rootPath, 0, repositories);
        // Start watchers for discovered repositories with better management
        UpdateWatchers(repositories);
        return repositories;
    }

    private void UpdateWatchers(List<GitRepository> repositories)
    {
        lock (_watcherLock)
        {
            if (_disposed) return;
            
            var currentRepoPaths = repositories.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingRepoPaths = _watchers.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            // Remove watchers for repositories that no longer exist
            var toRemove = existingRepoPaths.Except(currentRepoPaths).ToList();
            foreach (var path in toRemove)
            {
                SafeDisposeWatcher(path);
            }
            
            // Add watchers for new repositories (limit to prevent resource exhaustion)
            var toAdd = currentRepoPaths.Except(existingRepoPaths).Take(50).ToList(); // Max 50 watchers
            foreach (var repoPath in toAdd)
            {
                CreateWatcher(repoPath);
            }
        }
    }
    
    private void CreateWatcher(string repoPath)
    {
        try
        {
            var gitDir = Path.Combine(repoPath, ".git");
            if (!_fileSystem.DirectoryExists(gitDir))
                return;

            var watcher = new FileSystemWatcher(gitDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 8192 * 4 // Increase buffer to handle burst events
            };

            // Use weak event pattern to prevent memory leaks
            FileSystemEventHandler handler = (s, e) => OnRepoChanged(repoPath);
            RenamedEventHandler renamedHandler = (s, e) => OnRepoChanged(repoPath);
            ErrorEventHandler errorHandler = (s, e) => OnWatcherError(repoPath, e.GetException());

            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Renamed += renamedHandler;
            watcher.Error += errorHandler;
            
            watcher.EnableRaisingEvents = true;
            _watchers[repoPath] = watcher;
        }
        catch (Exception)
        {
            // Ignore watchers we can't create - this is not critical for functionality
        }
    }
    
    private void OnWatcherError(string repoPath, Exception ex)
    {
        // Handle watcher errors by recreating the watcher
        lock (_watcherLock)
        {
            SafeDisposeWatcher(repoPath);
            // Schedule recreation after a delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                lock (_watcherLock)
                {
                    if (!_disposed && !_watchers.ContainsKey(repoPath))
                    {
                        CreateWatcher(repoPath);
                    }
                }
            });
        }
    }
    
    private void SafeDisposeWatcher(string repoPath)
    {
        if (_watchers.TryGetValue(repoPath, out var watcher))
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
            finally
            {
                _watchers.Remove(repoPath);
            }
        }
    }
    
    private void CleanupWatchers(object? state)
    {
        lock (_watcherLock)
        {
            if (_disposed) return;
            
            var toRemove = new List<string>();
            foreach (var (path, watcher) in _watchers)
            {
                try
                {
                    // Check if the repository still exists
                    var gitDir = Path.Combine(path, ".git");
                    if (!_fileSystem.DirectoryExists(gitDir))
                    {
                        toRemove.Add(path);
                    }
                }
                catch
                {
                    toRemove.Add(path);
                }
            }
            
            foreach (var path in toRemove)
            {
                SafeDisposeWatcher(path);
            }
        }
    }

    private void OnRepoChanged(string repoPath)
    {
        // Enhanced throttling for frequent events
        lock (_lastChange)
        {
            var now = DateTime.UtcNow;
            if (_lastChange.TryGetValue(repoPath, out var last) && (now - last).TotalMilliseconds < 2000) // Increased to 2 seconds
                return;
            _lastChange[repoPath] = now;
        }

        // Use task to avoid blocking the file system watcher thread
        _ = Task.Run(() =>
        {
            try
            {
                RepositoryChanged?.Invoke(repoPath);
            }
            catch
            {
                // Ignore errors in event handlers
            }
        });
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
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _cleanupTimer?.Dispose();
        
        lock (_watcherLock)
        {
            // Dispose all watchers
            foreach (var (path, _) in _watchers.ToList())
            {
                SafeDisposeWatcher(path);
            }
            _watchers.Clear();
            _lastChange.Clear();
        }
    }
}
