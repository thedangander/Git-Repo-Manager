namespace GitRepoManager;

/// <summary>
/// Application configuration settings.
/// </summary>
public sealed class AppConfig
{
    public const int DefaultMaxScanDepth = 5;
    public const int DefaultRefreshIntervalSeconds = 300; // Increased to 5 minutes to reduce system load
    public const int GitCommandTimeoutMs = 15000; // Increased to 15 seconds for better reliability
    public const int KeyPollIntervalMs = 50;
    public const int KeyTimeoutMs = 1000;

    public string ScanPath { get; set; } = "";
    public int MaxScanDepth { get; init; } = DefaultMaxScanDepth;
    public int RefreshIntervalSeconds { get; init; } = DefaultRefreshIntervalSeconds;

    /// <summary>
    /// Directories to skip during scanning.
    /// </summary>
    public static readonly HashSet<string> IgnoredDirectories =
    [
        "node_modules",
        "bin",
        "obj",
        "packages",
        ".vs",
        "__pycache__",
        "venv",
        ".venv",
        "target",
        "build",
        "dist"
    ];
}
