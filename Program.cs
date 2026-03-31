using GitRepoManager;
using GitRepoManager.Abstractions;
using GitRepoManager.Services;
using GitRepoManager.UI;
using Spectre.Console;

// === Composition Root ===

var fileSystem = new FileSystemAdapter();
var processRunner = new ProcessRunner();

var config = new AppConfig
{
    ScanPath = GetScanPath(args, fileSystem)
};

if (!fileSystem.DirectoryExists(config.ScanPath))
{
    AnsiConsole.MarkupLine($"[red]Directory not found:[/] {Markup.Escape(config.ScanPath)}");
    return 1;
}

// Create services
var gitService = new GitService(processRunner);
var scanner = new RepositoryScanner(gitService, fileSystem, config.MaxScanDepth);
var externalApps = new ExternalAppService(processRunner);

// Create and run the Spectre.Console-based UI
var ui = new SpectreUI(config, gitService, scanner, externalApps, fileSystem);
ui.Run();

return 0;

static string GetScanPath(string[] args, IFileSystem fileSystem)
{
    if (args.Length > 0)
        return args[0];

    return AnsiConsole.Ask(
        "[cyan]Enter directory to scan[/] (or press Enter for current):",
        fileSystem.GetCurrentDirectory());
}
