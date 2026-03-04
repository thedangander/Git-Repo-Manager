# Git Repository Manager

A cross-platform terminal application for discovering and managing Git repositories on your local machine.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Repository Discovery** - Scans directories up to 5 levels deep to find all Git repositories
- **Repository Overview** - Displays for each repo:
  - Current branch
  - Latest commit (hash, message, relative time)
  - Ahead/behind status relative to remote
  - Size on disk
  - Uncommitted changes indicator
- **Auto-Refresh** - Automatically updates repository information every 30 seconds
- **Repository Actions**:
  - Sync (fetch + pull)
  - Switch branches (local and remote)
  - Fetch only
  - Open in terminal
  - Open in VS Code (if installed)
  - Delete local repository (with confirmation)
- **Batch Operations** - Fetch all repositories at once
- **Cross-Platform** - Works on Windows, macOS, and Linux

## Installation

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- Git installed and available in PATH

### Build from Source

```bash
git clone https://github.com/yourusername/git-repo-manager.git
cd git-repo-manager
dotnet build -c Release
```

### Run

```bash
# Run with prompts
dotnet run

# Scan specific directory
dotnet run -- /path/to/your/projects

# Or run the compiled executable
./bin/Release/net10.0/GitRepoManager /path/to/your/projects
```

### Publish as Standalone Executable

```bash
# For current platform
dotnet publish -c Release -o ./publish

# For specific platforms
dotnet publish -c Release -r win-x64 --self-contained -o ./publish/win
dotnet publish -c Release -r osx-x64 --self-contained -o ./publish/mac
dotnet publish -c Release -r linux-x64 --self-contained -o ./publish/linux
```

## Usage

### Main List View

| Key | Action |
|-----|--------|
| ↑/↓ | Navigate between repositories |
| Enter | Open repository actions menu |
| R | Rescan directory for repositories |
| F | Fetch all repositories |
| V | Open selected repo in VS Code (if installed) |
| Q | Quit |

### Repository Actions Menu

| Key | Action |
|-----|--------|
| 1 | Sync (fetch + pull) |
| 2 | Switch branch |
| 3 | Fetch only |
| 4 | Open in terminal |
| 5 | Open in VS Code (if installed) |
| 6 | Delete local repository |
| Esc/B | Back to main list |

### Visual Indicators

- `*` after repository name = has uncommitted changes
- Yellow repository name = has uncommitted changes
- Magenta ahead/behind = repository is out of sync with remote

## Screenshots

```
╔══════════════════════════════════════════════════════════════════════════════╗
║                         GIT REPOSITORY MANAGER                               ║
╚══════════════════════════════════════════════════════════════════════════════╝
Scanning: /Users/dev/projects

#   Repository                Branch               Latest Commit                  ↑↓         Size
────────────────────────────────────────────────────────────────────────────────────────────────────
►1  my-web-app*               main                 abc123 - Fix login bug (2h)    ↑1 ↓0      45.2 MB
 2  api-server                develop              def456 - Add endpoint (1d)     ↑0 ↓3      128 MB
 3  shared-utils              main                 ghi789 - Update deps (5d)      ↑0 ↓0      12.1 MB
────────────────────────────────────────────────────────────────────────────────────────────────────
 [↑/↓] Navigate   [Enter] Select   [R] Rescan   [F] Fetch All   [Q] Quit   [V] Open in VS Code

 * = has uncommitted changes | Auto-refresh in 25s
```

## Platform Support

| Feature | Windows | macOS | Linux |
|---------|---------|-------|-------|
| Repository scanning | ✅ | ✅ | ✅ |
| Git operations | ✅ | ✅ | ✅ |
| Open in terminal | cmd.exe | Terminal.app | gnome-terminal, konsole, xfce4-terminal, terminator, xterm |
| Open in VS Code | ✅ | ✅ | ✅ |

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
