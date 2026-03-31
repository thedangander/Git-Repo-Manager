using GitRepoManager.Models;

namespace GitRepoManager.UI;

public static class SpectreHelpers
{
    public static (string StatusIcon, string SyncStatus) GetRepoStatus(GitRepository repo)
    {
        var statusIcon = repo.HasUncommittedChanges ? "●" : "○";

        string syncStatus;
        if (!repo.HasRemote)
            syncStatus = "◌ local";
        else if (repo.Ahead > 0 && repo.Behind > 0)
            syncStatus = $"⇅ {repo.Ahead}/{repo.Behind}";
        else if (repo.Ahead > 0)
            syncStatus = $"↑ {repo.Ahead}";
        else if (repo.Behind > 0)
            syncStatus = $"↓ {repo.Behind}";
        else
            syncStatus = "✓ synced";

        return (statusIcon, syncStatus);
    }

    public static string GetStatusColor(GitRepository repo)
    {
        if (!repo.HasRemote) return "grey";
        if (repo.Ahead > 0 || repo.Behind > 0) return "yellow";
        return "green";
    }

    public static string GetSyncColor(GitRepository repo)
    {
        if (!repo.HasRemote) return "grey50";
        if (repo.Ahead > 0 && repo.Behind > 0) return "yellow";
        if (repo.Ahead > 0) return "yellow";
        if (repo.Behind > 0) return "red";
        return "green";
    }

    public static string GetChangesColor(GitRepository repo)
    {
        return repo.HasUncommittedChanges ? "red" : "green";
    }

    public static string FormatSize(long bytes)
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

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 1) + "…";
    }
}
