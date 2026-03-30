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

    public RepositoryScanner(IGitService git, IFileSystem fileSystem, int maxDepth = AppConfig.DefaultMaxScanDepth)
    {
        _git = git;
        _fileSystem = fileSystem;
        _maxDepth = maxDepth;
    }

    public event Action<string>? RepositoryFound;

    public List<GitRepository> Scan(string rootPath)
    {
        var repositories = new List<GitRepository>();
        ScanDirectory(rootPath, 0, repositories);
        return repositories;
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
