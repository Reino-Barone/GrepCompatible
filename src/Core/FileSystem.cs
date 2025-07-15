using System.Text;
using GrepCompatible.Abstractions;

namespace GrepCompatible.Core;

/// <summary>
/// 実際のファイルシステムを使用するFileSystemの実装
/// </summary>
public class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    
    public bool DirectoryExists(string path) => Directory.Exists(path);
    
    public IFileInfo GetFileInfo(string path) => new FileInfoWrapper(new FileInfo(path));
    
    public StreamReader OpenText(string path, Encoding encoding, int bufferSize = 4096)
    {
        var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        return new StreamReader(fileStream, encoding);
    }
    
    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        => new FileStream(path, mode, access, share, bufferSize, options);
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        => Directory.EnumerateFiles(path, searchPattern, searchOption);
    
    public StreamReader GetStandardInput() => new StreamReader(Console.OpenStandardInput());
}

/// <summary>
/// FileInfoのラッパークラス
/// </summary>
public class FileInfoWrapper(FileInfo fileInfo) : IFileInfo
{
    private readonly FileInfo _fileInfo = fileInfo ?? throw new ArgumentNullException(nameof(fileInfo));
    
    public long Length => _fileInfo.Length;
    public bool Exists => _fileInfo.Exists;
    public string FullName => _fileInfo.FullName;
    public string Name => _fileInfo.Name;
    public string? DirectoryName => _fileInfo.DirectoryName;
}

/// <summary>
/// パス操作の実装
/// </summary>
public class PathHelper : IPath
{
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    
    public string GetFileName(string path) => Path.GetFileName(path);
    
    public string Combine(params string[] paths) => Path.Combine(paths);
}
