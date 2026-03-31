using GitRepoManager.Abstractions;

namespace GitRepoManager.Services;

/// <summary>
/// Executes Git commands and parses their output.
/// </summary>
public sealed class GitService : IGitService
{
    private readonly IProcessRunner _processRunner;
    private readonly int _timeoutMs;

    public GitService(IProcessRunner processRunner, int timeoutMs = AppConfig.GitCommandTimeoutMs)
    {
        _processRunner = processRunner;
        _timeoutMs = timeoutMs;
    }

    public string RunCommand(string workingDir, string arguments)
    {
        var result = _processRunner.Run("git", arguments, workingDir, _timeoutMs);
        return result.Output;
    }

    public string GetCurrentBranch(string repoPath)
        => RunCommand(repoPath, "rev-parse --abbrev-ref HEAD").Trim();

    public string GetLatestCommit(string repoPath)
    {
        var result = RunCommand(repoPath, "log -1 --format=\"%h - %s (%ar)\"").Trim();
        return string.IsNullOrEmpty(result) ? "No commits" : result;
    }

    public (int ahead, int behind)? GetAheadBehind(string repoPath, string branch)
    {
        var tracking = RunCommand(repoPath, $"rev-parse --abbrev-ref {branch}@{{upstream}}").Trim();
        if (string.IsNullOrEmpty(tracking) || tracking.Contains("fatal"))
            return null;

        var counts = RunCommand(repoPath, $"rev-list --left-right --count {branch}...{tracking}").Trim();
        var parts = counts.Split('\t', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var ahead) &&
            int.TryParse(parts[1], out var behind))
        {
            return (ahead, behind);
        }

        return null;
    }

    public IReadOnlyList<string> GetBranches(string repoPath)
    {
        return RunCommand(repoPath, "branch -a")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().TrimStart('*').Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();
    }

    public bool HasUncommittedChanges(string repoPath)
        => !string.IsNullOrEmpty(RunCommand(repoPath, "status --porcelain").Trim());

    public void Fetch(string repoPath, bool prune = true)
    {
        var args = prune ? "fetch --all --prune" : "fetch --all";
        var res = _processRunner.Run("git", args, repoPath, _timeoutMs);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git fetch failed (exit={res.ExitCode} timedOut={res.TimedOut})\n{res.Output}");
    }

    public void Push(string repoPath)
    {
        var res = _processRunner.Run("git", "push", repoPath, _timeoutMs);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git push failed (exit={res.ExitCode} timedOut={res.TimedOut})\n{res.Output}");
    }

    public string Pull(string repoPath)
    {
        var res = _processRunner.Run("git", "pull", repoPath, _timeoutMs);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git pull failed (exit={res.ExitCode} timedOut={res.TimedOut})\n{res.Output}");
        return res.Output;
    }

    public string Checkout(string repoPath, string branch, bool createTracking = false)
    {
        var command = createTracking
            ? $"checkout -b {branch} origin/{branch}"
            : $"checkout {branch}";

        var res = _processRunner.Run("git", command, repoPath, _timeoutMs);
        if (res.ExitCode != 0)
        {
            // If branch already exists locally and createTracking was requested, try plain checkout
            if (createTracking && res.Output.Contains("already exists"))
            {
                var r2 = _processRunner.Run("git", $"checkout {branch}", repoPath, _timeoutMs);
                if (r2.ExitCode != 0)
                    throw new InvalidOperationException($"git checkout failed (exit={r2.ExitCode} timedOut={r2.TimedOut})\n{r2.Output}");
                return r2.Output;
            }

            throw new InvalidOperationException($"git checkout failed (exit={res.ExitCode} timedOut={res.TimedOut})\n{res.Output}");
        }

        return res.Output;
    }
}
