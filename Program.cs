using System.Text;
using GitRepoManager;
using GitRepoManager.Abstractions;
using GitRepoManager.Services;
using GitRepoManager.UI;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "Git Repository Manager";

// === Composition Root ===
// Create all dependencies and wire them together

var console = new SystemConsoleIO();
var fileSystem = new FileSystemAdapter();
var clock = new SystemClock();
var processRunner = new ProcessRunner();

var config = new AppConfig
{
    ScanPath = GetScanPath(args, console, fileSystem)
};

if (!fileSystem.DirectoryExists(config.ScanPath))
{
    console.SetForegroundColor(ConsoleColor.Red);
    console.WriteLine($"Directory not found: {config.ScanPath}");
    console.ResetColor();
    return 1;
}

// Create services with injected dependencies
var gitService = new GitService(processRunner);
var scanner = new RepositoryScanner(gitService, fileSystem, config.MaxScanDepth);
var externalApps = new ExternalAppService(processRunner);
var renderer = new ConsoleRenderer(console);

// Create and run application
var app = new Application(
    config,
    gitService,
    scanner,
    externalApps,
    renderer,
    clock,
    fileSystem,
    console);

app.Run();
return 0;

static string GetScanPath(string[] args, IConsoleIO console, IFileSystem fileSystem)
{
    if (args.Length > 0)
        return args[0];

    console.Write("Enter directory to scan (or press Enter for current directory): ");
    var input = console.ReadLine();

    return string.IsNullOrWhiteSpace(input)
        ? fileSystem.GetCurrentDirectory()
        : input;
}
