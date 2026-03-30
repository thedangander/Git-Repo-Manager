namespace GitRepoManager.Models;

/// <summary>
/// Represents a local Git repository with its current state.
/// </summary>
public sealed class GitRepository
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string CurrentBranch { get; set; } = "";
    public string LatestCommit { get; set; } = "";
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public bool HasRemote { get; set; }
    public long SizeOnDisk { get; set; }
    public IReadOnlyList<string> Branches { get; set; } = [];
    public bool HasUncommittedChanges { get; set; }

    public bool HasUnpushedWork => HasUncommittedChanges || Ahead > 0;
}
