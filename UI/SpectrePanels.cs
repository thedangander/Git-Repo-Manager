using GitRepoManager.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GitRepoManager.UI;

public static class SpectrePanels
{
    public static Panel BuildHeader(int repoCount, bool isScanning = false, int scanFound = 0)
    {
        var headerGrid = new Grid()
            .AddColumn(new GridColumn())
            .AddColumn(new GridColumn());

        headerGrid.AddRow(new Markup("[bold white]Git Repository Manager[/]"), new Markup(" "));
        headerGrid.AddRow(new Markup("[grey]Browse, sync, and manage git repos with a modern terminal UI[/]"), new Markup(" "));
        if (isScanning)
        {
            headerGrid.AddRow(new Markup($"[yellow]Scanning... found {scanFound} repos[/]"), new Markup(" "));
        }
        headerGrid.AddRow(new Markup("[grey]Quick keys: [cyan]↑↓[/] Navigate  [cyan]Enter[/] Sync  [cyan]D[/] Delete  [cyan]I[/] Details  [cyan]R[/] Rescan  [cyan]Q[/] Quit[/]"), new Markup(" "));

        return new Panel(headerGrid)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey37),
            Padding = new Padding(1, 1),
            Header = new PanelHeader($"[grey50]Status: Ready | repos: {repoCount}[/]"),
            Expand = true
        };
    }

    public static Panel BuildRepositoryPanel(List<GitRepository> repositories, int selectedIndex, int scrollTop, int maxRows)
    {
        // Simpler, more robust repository table: avoid strict column widths
        var dashboardWidth = Math.Max(60, Console.WindowWidth - 4);
        var repoColMax = Math.Max(16, dashboardWidth / 4);
        var branchColMax = Math.Max(12, dashboardWidth / 6);
        var commitColMax = 12;

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());

        // Header row
        grid.AddRow(
            new Markup("[bold]Repository[/]"),
            new Markup("[bold]Branch[/]"),
            new Markup("[bold]Commit[/]"),
            new Markup("[bold]Sync[/]"),
            new Markup("[bold]Size[/]"),
            new Markup("[bold]Changes[/]")
        );

        if (repositories.Count == 0)
        {
            grid.AddRow(new Markup("[grey italic]No repositories found[/]"), new Markup(""), new Markup(""), new Markup(""), new Markup(""), new Markup(""));
        }
        else
        {
            var maxIdx = Math.Min(repositories.Count, scrollTop + maxRows);
            for (var i = scrollTop; i < maxIdx; i++)
            {
                var repo = repositories[i];
                var isSelected = i == selectedIndex;

                var (statusIcon, syncStatus) = SpectreHelpers.GetRepoStatus(repo);
                var statusColor = SpectreHelpers.GetStatusColor(repo);

                var repoName = SpectreHelpers.Truncate(repo.Name, repoColMax);
                var branchName = SpectreHelpers.Truncate(repo.CurrentBranch, branchColMax);
                var commitShort = SpectreHelpers.Truncate(repo.LatestCommit, commitColMax);
                var aheadBehind = repo.HasRemote ? $"{repo.Ahead}/{repo.Behind}" : "local";
                var sizeText = SpectreHelpers.FormatSize(repo.SizeOnDisk);
                var repoCell = isSelected ? $"[black on grey37]{Markup.Escape(repoName)}[/]" : $"[grey70]{Markup.Escape(repoName)}[/]";
                var branchCell = isSelected ? $"[black on grey37]{Markup.Escape(branchName)}[/]" : $"[grey58]{Markup.Escape(branchName)}[/]";
                var commitCell = isSelected ? $"[black on grey37]{Markup.Escape(commitShort)}[/]" : $"[grey58]{Markup.Escape(commitShort)}[/]";
                var syncColor = SpectreHelpers.GetSyncColor(repo);
                var syncCell = isSelected ? $"[black on grey37]{Markup.Escape(syncStatus)}[/]" : $"[{syncColor}]{Markup.Escape(syncStatus)}[/]";
                var sizeCell = isSelected ? $"[black on grey37]{Markup.Escape(sizeText)}[/]" : $"[grey]{Markup.Escape(sizeText)}[/]";

                string changesCellMarkup;
                if (repo.HasUncommittedChanges)
                {
                    var changesColor = SpectreHelpers.GetChangesColor(repo);
                    var changesText = "uncommitted changes";
                    changesCellMarkup = isSelected ? $"[black on grey37]{Markup.Escape(changesText)}[/]" : $"[{changesColor}]{Markup.Escape(changesText)}[/]";
                }
                else
                {
                    changesCellMarkup = ""; // show nothing when clean
                }

                grid.AddRow(new Markup(repoCell), new Markup(branchCell), new Markup(commitCell), new Markup(syncCell), new Markup(sizeCell), new Markup(changesCellMarkup));
            }
        }

        return new Panel(grid)
        {
            Header = new PanelHeader("[bold slateblue3]Repositories[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey37),
            Padding = new Padding(1, 1),
            Expand = true
        };
    }

    public static Panel BuildDetailsPanel(List<GitRepository> repositories, int selectedIndex)
    {
        IRenderable content;

        if (repositories.Count == 0 || selectedIndex < 0 || selectedIndex >= repositories.Count)
        {
            content = new Markup("[grey]Select a repository to view details[/]");
        }
        else
        {
            var repo = repositories[selectedIndex];
            var details = new Rows(
                new Markup($"[bold cyan]◆ {Markup.Escape(repo.Name)}[/]"),
                new Markup(""),
                new Markup($"[grey]📁 Path:[/]    [white]{Markup.Escape(repo.Path)}[/]"),
                new Markup($"[grey]⎇  Branch:[/]  [magenta]{Markup.Escape(repo.CurrentBranch)}[/]"),
                new Markup($"[grey]⚡ Commit:[/]  [white]{Markup.Escape(repo.LatestCommit)}[/]"),
                new Markup($"[grey]💾 Size:[/]    [white]{SpectreHelpers.FormatSize(repo.SizeOnDisk)}[/]"),
                new Markup("")
            );

            var rows = new List<IRenderable> { details };

            if (repo.HasRemote)
            {
                var aheadColor = repo.Ahead > 0 ? "yellow" : "green";
                var behindColor = repo.Behind > 0 ? "yellow" : "green";
                rows.Add(new Markup($"[{aheadColor}]   ↑ Ahead:  {repo.Ahead} commits[/]"));
                rows.Add(new Markup($"[{behindColor}]   ↓ Behind: {repo.Behind} commits[/]"));
            }
            else
            {
                rows.Add(new Markup("[grey]   ◌ Local repository (no remote)[/]") );
            }

            if (repo.HasUncommittedChanges)
            {
                rows.Add(new Markup(""));
                rows.Add(new Markup("[bold red]   ● Uncommitted changes[/]"));
            }

            if (repo.Ahead > 0)
            {
                rows.Add(new Markup($"[yellow]   ▲ {repo.Ahead} unpushed commit(s)[/]") );
            }

            rows.Add(new Markup(""));
            // Render branches as hierarchical tree with Local and Remotes groups
            var branchRoot = new Tree(new Markup("[cyan]Branches[/]"));
            var localRoot = branchRoot.AddNode(new Markup("[green]Local[/]"));
            var remotesRoot = branchRoot.AddNode(new Markup("[yellow]Remotes[/]"));

            var localMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            var remoteMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var branch in repo.Branches)
            {
                if (branch.StartsWith("remotes/"))
                {
                    // remotes/origin/feature/x -> under Remotes -> origin -> feature -> x
                    var rest = branch.Substring("remotes/".Length);
                    var parts = rest.Split('/');
                    var acc = "";
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        acc = acc.Length == 0 ? parts[i] : acc + "/" + parts[i];
                        if (!remoteMap.TryGetValue(acc, out var node))
                        {
                            var parentKey = acc.Contains('/') ? acc.Substring(0, acc.LastIndexOf('/')) : "";
                            TreeNode parent = string.IsNullOrEmpty(parentKey) ? remotesRoot : remoteMap[parentKey];
                            node = parent.AddNode(new Markup($"[white]{Markup.Escape(parts[i])}[/]"));
                            remoteMap[acc] = node;
                        }
                    }

                    var leaf = parts.Last();
                    var leafLabel = branch == repo.CurrentBranch ? new Markup($"[magenta]→ {Markup.Escape(leaf)}[/]") : new Markup($"[white]{Markup.Escape(leaf)}[/]");
                    if (parts.Length == 1) remotesRoot.AddNode(leafLabel); else remoteMap[string.Join('/', parts.Take(parts.Length - 1))].AddNode(leafLabel);
                }
                else
                {
                    // local branches: feature/x -> Local -> feature -> x
                    var parts = branch.Split('/');
                    var acc = "";
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        acc = acc.Length == 0 ? parts[i] : acc + "/" + parts[i];
                        if (!localMap.TryGetValue(acc, out var node))
                        {
                            var parentKey = acc.Contains('/') ? acc.Substring(0, acc.LastIndexOf('/')) : "";
                            TreeNode parent = string.IsNullOrEmpty(parentKey) ? localRoot : localMap[parentKey];
                            node = parent.AddNode(new Markup($"[white]{Markup.Escape(parts[i])}[/]"));
                            localMap[acc] = node;
                        }
                    }

                    var leaf = parts.Last();
                    var leafLabel = branch == repo.CurrentBranch ? new Markup($"[magenta]→ {Markup.Escape(leaf)}[/]") : new Markup($"[white]{Markup.Escape(leaf)}[/]");
                    if (parts.Length == 1) localRoot.AddNode(leafLabel); else localMap[string.Join('/', parts.Take(parts.Length - 1))].AddNode(leafLabel);
                }
            }

            rows.Add(branchRoot);

            rows.Add(new Markup(""));

            content = new Rows(rows);
        }

        return new Panel(content)
        {
            Header = new PanelHeader("[bold slateblue3]Details[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey37),
            Padding = new Padding(1, 1),
            Expand = true
        };
    }

    public static Panel BuildStatusBar(bool isVsCodeInstalled, int repoCount)
    {
        var shortcuts = new List<string>
        {
            "[cyan]↑↓[/] Navigate",
            "[cyan]Enter[/] Sync",
            "[cyan]D[/] Delete",
            "[cyan]I[/] Details",
            "[cyan]B[/] Branch",
            "[cyan]T[/] Terminal"
        };

        if (isVsCodeInstalled)
            shortcuts.Add("[cyan]V[/] VS Code");

        shortcuts.Add("[cyan]R[/] Rescan");
        shortcuts.Add("[cyan]F[/] Fetch All");
        shortcuts.Add("[cyan]Q[/] Quit");

        var footer = new Markup($"[grey]repos: {repoCount}[/]");
        var grid = new Grid().AddColumn(new GridColumn().NoWrap())
            .AddRow(new Markup(string.Join("  │  ", shortcuts)))
            .AddRow(footer);

        return new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey37),
            Padding = new Padding(1, 1)
        };
    }
}
