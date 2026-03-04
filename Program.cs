using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GitRepoManager
{
    class Program
    {
        static List<GitRepository> _repositories = new();
        static int _selectedIndex = 0;
        static string _scanPath = "";
        static int _maxDepth = 5;
        static int _refreshIntervalSeconds = 30;
        static DateTime _lastRefresh = DateTime.MinValue;
        static bool _isRefreshing = false;
        static bool _hasVSCode = false;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Git Repository Manager";

            // Get scan path from args or prompt
            if (args.Length > 0)
            {
                _scanPath = args[0];
            }
            else
            {
                Console.Write("Enter directory to scan (or press Enter for current directory): ");
                _scanPath = Console.ReadLine() ?? "";
                if (string.IsNullOrWhiteSpace(_scanPath))
                {
                    _scanPath = Directory.GetCurrentDirectory();
                }
            }

            if (!Directory.Exists(_scanPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Directory not found: {_scanPath}");
                Console.ResetColor();
                return;
            }

            // Check if VS Code is installed
            _hasVSCode = CheckVSCodeInstalled();

            ScanForRepositories();
            RunInteractiveMode();
        }

        static void ScanForRepositories()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("🔍 Scanning for Git repositories...");
            Console.ResetColor();

            _repositories.Clear();
            _selectedIndex = 0;

            ScanDirectory(_scanPath, 0);

            Console.WriteLine($"Found {_repositories.Count} repositories.\n");
        }

        static void ScanDirectory(string path, int depth)
        {
            if (depth > _maxDepth) return;

            try
            {
                string gitDir = Path.Combine(path, ".git");
                if (Directory.Exists(gitDir))
                {
                    var repo = GetRepositoryInfo(path);
                    if (repo != null)
                    {
                        _repositories.Add(repo);
                        Console.WriteLine($"  Found: {repo.Name}");
                    }
                    return; // Don't scan inside git repos
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirName = Path.GetFileName(dir);
                    // Skip hidden directories and common non-repo folders
                    if (dirName.StartsWith(".") || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
                        continue;

                    ScanDirectory(dir, depth + 1);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Warning: Could not access {path}: {ex.Message}");
                Console.ResetColor();
            }
        }

        static GitRepository? GetRepositoryInfo(string repoPath)
        {
            try
            {
                var repo = new GitRepository
                {
                    Path = repoPath,
                    Name = Path.GetFileName(repoPath)
                };

                // Get current branch
                repo.CurrentBranch = RunGitCommand(repoPath, "rev-parse --abbrev-ref HEAD").Trim();

                // Get latest commit
                var commitInfo = RunGitCommand(repoPath, "log -1 --format=\"%h - %s (%ar)\"").Trim();
                repo.LatestCommit = string.IsNullOrEmpty(commitInfo) ? "No commits" : commitInfo;

                // Get ahead/behind info (requires remote tracking)
                var trackingBranch = RunGitCommand(repoPath, $"rev-parse --abbrev-ref {repo.CurrentBranch}@{{upstream}}").Trim();
                
                if (!string.IsNullOrEmpty(trackingBranch) && !trackingBranch.Contains("fatal"))
                {
                    // Fetch to get latest remote info (silently)
                    var aheadBehind = RunGitCommand(repoPath, $"rev-list --left-right --count {repo.CurrentBranch}...{trackingBranch}").Trim();
                    var parts = aheadBehind.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (int.TryParse(parts[0], out int ahead))
                            repo.Ahead = ahead;
                        if (int.TryParse(parts[1], out int behind))
                            repo.Behind = behind;
                    }
                    repo.HasRemote = true;
                }
                else
                {
                    repo.HasRemote = false;
                }

                // Get repo size
                repo.SizeOnDisk = GetDirectorySize(repoPath);

                // Get list of branches
                var branches = RunGitCommand(repoPath, "branch -a").Trim();
                repo.Branches = branches.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim().TrimStart('*').Trim())
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .ToList();

                // Check for uncommitted changes
                var status = RunGitCommand(repoPath, "status --porcelain").Trim();
                repo.HasUncommittedChanges = !string.IsNullOrEmpty(status);

                return repo;
            }
            catch
            {
                return null;
            }
        }

        static string RunGitCommand(string workingDir, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return output;
            }
            catch
            {
                return "";
            }
        }

        static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    size += file.Length;
                }
            }
            catch
            {
                // Ignore access errors
            }
            return size;
        }

        static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        static void RunInteractiveMode()
        {
            bool running = true;
            _lastRefresh = DateTime.Now;

            while (running)
            {
                DisplayRepositories();
                DisplayMenu();

                // Check for auto-refresh
                var refreshNeeded = (DateTime.Now - _lastRefresh).TotalSeconds >= _refreshIntervalSeconds;
                
                // Poll for key input with timeout to allow periodic refresh
                var keyPressed = WaitForKeyWithTimeout(1000);
                
                if (keyPressed.HasValue)
                {
                    switch (keyPressed.Value)
                    {
                        case ConsoleKey.UpArrow:
                            if (_selectedIndex > 0) _selectedIndex--;
                            break;

                        case ConsoleKey.DownArrow:
                            if (_selectedIndex < _repositories.Count - 1) _selectedIndex++;
                            break;

                        case ConsoleKey.Enter:
                            if (_repositories.Count > 0)
                                ShowRepoActions();
                            _lastRefresh = DateTime.Now; // Reset timer after action
                            break;

                        case ConsoleKey.R:
                            ScanForRepositories();
                            _lastRefresh = DateTime.Now;
                            break;

                        case ConsoleKey.F:
                            FetchAllRepositories();
                            _lastRefresh = DateTime.Now;
                            break;

                        case ConsoleKey.Q:
                            running = false;
                            break;

                        case ConsoleKey.V:
                            if (_hasVSCode && _repositories.Count > 0)
                            {
                                OpenInVSCode(_repositories[_selectedIndex]);
                            }
                            break;
                    }
                }
                else if (refreshNeeded && !_isRefreshing)
                {
                    // Auto-refresh repository info
                    RefreshAllRepositoryInfo();
                    _lastRefresh = DateTime.Now;
                }
            }

            Console.Clear();
            Console.WriteLine("Goodbye!");
        }

        static ConsoleKey? WaitForKeyWithTimeout(int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (Console.KeyAvailable)
                {
                    return Console.ReadKey(true).Key;
                }
                Thread.Sleep(50); // Small sleep to reduce CPU usage
            }
            return null;
        }

        static void RefreshAllRepositoryInfo()
        {
            _isRefreshing = true;
            try
            {
                for (int i = 0; i < _repositories.Count; i++)
                {
                    var updatedRepo = GetRepositoryInfo(_repositories[i].Path);
                    if (updatedRepo != null)
                    {
                        _repositories[i] = updatedRepo;
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        static void DisplayRepositories()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                         GIT REPOSITORY MANAGER                               ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Scanning: {_scanPath}");
            Console.WriteLine();
            Console.ResetColor();

            if (_repositories.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No Git repositories found in this directory.");
                Console.ResetColor();
                return;
            }

            // Header
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{"#",-3} {"Repository",-25} {"Branch",-20} {"Latest Commit",-30} {"↑↓",-10} {"Size",-10}");
            Console.WriteLine(new string('─', 100));
            Console.ResetColor();

            for (int i = 0; i < _repositories.Count; i++)
            {
                var repo = _repositories[i];
                bool isSelected = i == _selectedIndex;

                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkBlue;
                    Console.ForegroundColor = ConsoleColor.White;
                }

                string indicator = isSelected ? "►" : " ";
                string name = TruncateString(repo.Name, 24);
                string branch = TruncateString(repo.CurrentBranch, 19);
                string commit = TruncateString(repo.LatestCommit, 29);

                string aheadBehind;
                if (!repo.HasRemote)
                {
                    aheadBehind = "No remote";
                }
                else
                {
                    aheadBehind = $"↑{repo.Ahead} ↓{repo.Behind}";
                }

                string size = FormatSize(repo.SizeOnDisk);

                // Status indicators
                string statusIndicator = "";
                if (repo.HasUncommittedChanges)
                {
                    statusIndicator = "*";
                }

                Console.Write($"{indicator}{i + 1,-2} ");

                // Name with status
                if (repo.HasUncommittedChanges && !isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                Console.Write($"{name + statusIndicator,-25} ");

                // Branch
                if (!isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.Write($"{branch,-20} ");

                // Commit
                if (!isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                Console.Write($"{commit,-30} ");

                // Ahead/Behind
                if (!isSelected)
                {
                    if (repo.Ahead > 0 || repo.Behind > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }
                }
                Console.Write($"{aheadBehind,-10} ");

                // Size
                if (!isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                }
                Console.Write($"{size,-10}");

                Console.ResetColor();
                Console.WriteLine();
            }

            Console.WriteLine();
        }

        static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Length <= maxLength ? str : str.Substring(0, maxLength - 2) + "..";
        }

        static void DisplayMenu()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('─', 100));
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" [↑/↓] ");
            Console.ResetColor();
            Console.Write("Navigate  ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" [Enter] ");
            Console.ResetColor();
            Console.Write("Select  ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" [R] ");
            Console.ResetColor();
            Console.Write("Rescan  ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" [F] ");
            Console.ResetColor();
            Console.Write("Fetch All  ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" [Q] ");
            Console.ResetColor();
            Console.Write("Quit");

            if (_hasVSCode)
            {
                Console.Write("  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(" [V] ");
                Console.ResetColor();
                Console.Write("Open in VS Code");
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            var secondsUntilRefresh = Math.Max(0, _refreshIntervalSeconds - (int)(DateTime.Now - _lastRefresh).TotalSeconds);
            Console.WriteLine($"\n * = has uncommitted changes | Auto-refresh in {secondsUntilRefresh}s");
            Console.ResetColor();
        }

        static void ShowRepoActions()
        {
            if (_repositories.Count == 0 || _selectedIndex >= _repositories.Count)
                return;

            var repo = _repositories[_selectedIndex];
            bool inActionMenu = true;

            while (inActionMenu)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║  Repository: {repo.Name,-63} ║");
                Console.WriteLine($"╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"  Path:    {repo.Path}");
                Console.WriteLine($"  Branch:  {repo.CurrentBranch}");
                Console.WriteLine($"  Commit:  {repo.LatestCommit}");
                Console.WriteLine($"  Size:    {FormatSize(repo.SizeOnDisk)}");
                if (repo.HasRemote)
                {
                    Console.WriteLine($"  Status:  {repo.Ahead} ahead, {repo.Behind} behind");
                }
                else
                {
                    Console.WriteLine($"  Status:  No remote tracking branch");
                }
                if (repo.HasUncommittedChanges)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠ Has uncommitted changes");
                }
                Console.ResetColor();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(new string('─', 80));
                Console.ResetColor();
                Console.WriteLine();

                Console.WriteLine("  Select an action:");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  [1] ");
                Console.ResetColor();
                Console.WriteLine("Sync (fetch + pull)");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  [2] ");
                Console.ResetColor();
                Console.WriteLine("Switch branch");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  [3] ");
                Console.ResetColor();
                Console.WriteLine("Fetch only");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  [4] ");
                Console.ResetColor();
                Console.WriteLine("Open in terminal");

                if (_hasVSCode)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("  [5] ");
                    Console.ResetColor();
                    Console.WriteLine("Open in VS Code");
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [6] ");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Delete local repository");
                Console.ResetColor();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  [Esc/B] ");
                Console.ResetColor();
                Console.WriteLine("Back to list");

                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                    case ConsoleKey.NumPad1:
                        SyncRepository(repo);
                        RefreshRepositoryInfo(repo);
                        break;

                    case ConsoleKey.D2:
                    case ConsoleKey.NumPad2:
                        SwitchBranch(repo);
                        RefreshRepositoryInfo(repo);
                        break;

                    case ConsoleKey.D3:
                    case ConsoleKey.NumPad3:
                        FetchRepository(repo);
                        RefreshRepositoryInfo(repo);
                        break;

                    case ConsoleKey.D4:
                    case ConsoleKey.NumPad4:
                        OpenInTerminal(repo);
                        break;

                    case ConsoleKey.D5:
                    case ConsoleKey.NumPad5:
                        if (_hasVSCode)
                        {
                            OpenInVSCode(repo);
                        }
                        else
                        {
                            if (DeleteRepository(repo))
                            {
                                inActionMenu = false;
                            }
                        }
                        break;

                    case ConsoleKey.D6:
                    case ConsoleKey.NumPad6:
                        if (DeleteRepository(repo))
                        {
                            inActionMenu = false;
                        }
                        break;

                    case ConsoleKey.Escape:
                    case ConsoleKey.B:
                        inActionMenu = false;
                        break;
                }
            }
        }

        static void SyncRepository(GitRepository repo)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Syncing {repo.Name}...\n");
            Console.ResetColor();

            Console.WriteLine("Fetching...");
            var fetchResult = RunGitCommand(repo.Path, "fetch --all");
            Console.WriteLine(string.IsNullOrEmpty(fetchResult) ? "Fetch complete." : fetchResult);

            Console.WriteLine("\nPulling...");
            var pullResult = RunGitCommand(repo.Path, "pull");
            Console.WriteLine(string.IsNullOrEmpty(pullResult) ? "Already up to date." : pullResult);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✓ Sync complete!");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static void FetchRepository(GitRepository repo)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Fetching {repo.Name}...\n");
            Console.ResetColor();

            var result = RunGitCommand(repo.Path, "fetch --all --prune");
            Console.WriteLine(string.IsNullOrEmpty(result) ? "Fetch complete." : result);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✓ Fetch complete!");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static void FetchAllRepositories()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Fetching all repositories...\n");
            Console.ResetColor();

            foreach (var repo in _repositories)
            {
                Console.Write($"  Fetching {repo.Name}... ");
                RunGitCommand(repo.Path, "fetch --all --prune");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✓ All repositories fetched!");
            Console.ResetColor();

            // Refresh all repo info
            for (int i = 0; i < _repositories.Count; i++)
            {
                var updatedRepo = GetRepositoryInfo(_repositories[i].Path);
                if (updatedRepo != null)
                {
                    _repositories[i] = updatedRepo;
                }
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static void SwitchBranch(GitRepository repo)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Switch branch for {repo.Name}\n");
            Console.ResetColor();

            Console.WriteLine("Available branches:");
            Console.WriteLine();

            var localBranches = repo.Branches
                .Where(b => !b.StartsWith("remotes/"))
                .ToList();

            var remoteBranches = repo.Branches
                .Where(b => b.StartsWith("remotes/") && !b.Contains("HEAD"))
                .Select(b => b.Replace("remotes/origin/", "").Replace("remotes/", ""))
                .Distinct()
                .Where(b => !localBranches.Contains(b))
                .ToList();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  Local branches:");
            Console.ResetColor();
            for (int i = 0; i < localBranches.Count; i++)
            {
                string current = localBranches[i] == repo.CurrentBranch ? " (current)" : "";
                Console.WriteLine($"    {i + 1}. {localBranches[i]}{current}");
            }

            if (remoteBranches.Count > 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Remote branches (will create local tracking branch):");
                Console.ResetColor();
                for (int i = 0; i < remoteBranches.Count; i++)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    {localBranches.Count + i + 1}. {remoteBranches[i]}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.Write("Enter branch number (or 0 to cancel): ");

            if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0)
            {
                string branchName;
                bool isRemote = false;

                if (choice <= localBranches.Count)
                {
                    branchName = localBranches[choice - 1];
                }
                else if (choice <= localBranches.Count + remoteBranches.Count)
                {
                    branchName = remoteBranches[choice - localBranches.Count - 1];
                    isRemote = true;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid selection.");
                    Console.ResetColor();
                    Console.ReadKey(true);
                    return;
                }

                Console.WriteLine();
                Console.Write($"Switching to {branchName}... ");

                string command = isRemote ? $"checkout -b {branchName} origin/{branchName}" : $"checkout {branchName}";
                var result = RunGitCommand(repo.Path, command);

                // Check if we need to track instead
                if (isRemote && result.Contains("already exists"))
                {
                    result = RunGitCommand(repo.Path, $"checkout {branchName}");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    Console.WriteLine(result);
                }
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static void OpenInTerminal(GitRepository repo)
        {
            try
            {
                ProcessStartInfo startInfo;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: open cmd or PowerShell
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k cd /d \"{repo.Path}\"",
                        UseShellExecute = true
                    };
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS: open Terminal.app
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"-a Terminal \"{repo.Path}\"",
                        UseShellExecute = false
                    };
                }
                else
                {
                    // Linux: try common terminal emulators
                    string? terminal = GetLinuxTerminal();
                    if (terminal == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\nNo supported terminal emulator found.");
                        Console.ResetColor();
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(true);
                        return;
                    }

                    startInfo = new ProcessStartInfo
                    {
                        FileName = terminal,
                        Arguments = terminal switch
                        {
                            "gnome-terminal" => $"--working-directory=\"{repo.Path}\"",
                            "konsole" => $"--workdir \"{repo.Path}\"",
                            "xfce4-terminal" => $"--working-directory=\"{repo.Path}\"",
                            "xterm" => $"-e \"cd '{repo.Path}' && $SHELL\"",
                            "terminator" => $"--working-directory=\"{repo.Path}\"",
                            _ => $"--working-directory=\"{repo.Path}\""
                        },
                        UseShellExecute = false
                    };
                }

                Process.Start(startInfo);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Opened in Terminal");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nFailed to open terminal: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static string? GetLinuxTerminal()
        {
            string[] terminals = { "gnome-terminal", "konsole", "xfce4-terminal", "terminator", "xterm" };
            foreach (var term in terminals)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = term,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit(1000);
                        if (!string.IsNullOrEmpty(output) && process.ExitCode == 0)
                            return term;
                    }
                }
                catch { }
            }
            return null;
        }

        static bool CheckVSCodeInstalled()
        {
            try
            {
                ProcessStartInfo startInfo;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c where code",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = "code",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(2000);
                return !string.IsNullOrEmpty(output) && process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void OpenInVSCode(GitRepository repo)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{repo.Path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Opened in VS Code");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nFailed to open VS Code: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }

        static bool DeleteRepository(GitRepository repo)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                              ⚠ WARNING ⚠                                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine($"  You are about to DELETE the following repository:");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  {repo.Path}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"  Size: {FormatSize(repo.SizeOnDisk)}");

            if (repo.HasUncommittedChanges)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  ⚠ This repository has UNCOMMITTED CHANGES that will be LOST!");
                Console.ResetColor();
            }

            if (repo.Ahead > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  ⚠ This repository has {repo.Ahead} UNPUSHED COMMITS that will be LOST!");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  THIS ACTION CANNOT BE UNDONE!");
            Console.ResetColor();

            Console.WriteLine();
            Console.Write("  Type the repository name to confirm deletion: ");

            string? confirmation = Console.ReadLine();

            if (confirmation == repo.Name)
            {
                Console.WriteLine();
                Console.Write("  Deleting...");

                try
                {
                    // Use recursive delete
                    DeleteDirectoryRecursive(repo.Path);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" ✓ Deleted!");
                    Console.ResetColor();

                    _repositories.Remove(repo);
                    if (_selectedIndex >= _repositories.Count && _selectedIndex > 0)
                    {
                        _selectedIndex--;
                    }

                    Console.WriteLine("\n  Press any key to continue...");
                    Console.ReadKey(true);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($" Failed: {ex.Message}");
                    Console.ResetColor();
                    Console.WriteLine("\n  Press any key to continue...");
                    Console.ReadKey(true);
                    return false;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n  Deletion cancelled.");
                Console.ResetColor();
                Console.WriteLine("\n  Press any key to continue...");
                Console.ReadKey(true);
                return false;
            }
        }

        static void DeleteDirectoryRecursive(string path)
        {
            // Handle read-only files (common in .git directories)
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, true);
        }

        static void RefreshRepositoryInfo(GitRepository repo)
        {
            var index = _repositories.IndexOf(repo);
            if (index >= 0)
            {
                var updatedRepo = GetRepositoryInfo(repo.Path);
                if (updatedRepo != null)
                {
                    _repositories[index] = updatedRepo;
                }
            }
        }
    }

    class GitRepository
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string CurrentBranch { get; set; } = "";
        public string LatestCommit { get; set; } = "";
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public bool HasRemote { get; set; }
        public long SizeOnDisk { get; set; }
        public List<string> Branches { get; set; } = new();
        public bool HasUncommittedChanges { get; set; }
    }
}
