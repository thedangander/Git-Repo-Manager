namespace GitRepoManager.Abstractions;

/// <summary>
/// Abstracts file system operations for testability.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    string GetCurrentDirectory();
    IEnumerable<string> GetDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option);
    void DeleteDirectory(string path, bool recursive);
    void DeleteDirectoryRecursive(string path);
    void SetFileAttributes(string path, FileAttributes attributes);
    long GetFileLength(string path);
}

/// <summary>
/// Default implementation using System.IO.
/// </summary>
public sealed class FileSystemAdapter : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();

    public IEnumerable<string> GetDirectories(string path) => Directory.GetDirectories(path);

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        => Directory.EnumerateFiles(path, pattern, option);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    /// <summary>
    /// Recursively deletes a directory, handling read-only files (common with .git directories).
    /// </summary>
    public void DeleteDirectoryRecursive(string path)
    {
        if (!Directory.Exists(path)) return;

        // Clear read-only attributes on all files (git creates read-only files)
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        // Clear attributes on directories
        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(dir, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    public void SetFileAttributes(string path, FileAttributes attributes)
        => File.SetAttributes(path, attributes);

    public long GetFileLength(string path) => new FileInfo(path).Length;
}
