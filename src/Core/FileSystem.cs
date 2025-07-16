using System.Text;
using GrepCompatible.Abstractions;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// ファイルから行を非同期で読み込む（ReadOnlyMemoryを使用したゼロコピー処理）
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<char>> ReadLinesAsMemoryAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fileInfo = GetFileInfo(path);
        var bufferSize = GetOptimalBufferSize(fileInfo.Length);
        
        using var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize);
        
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line.AsMemory();
        }
    }

    /// <summary>
    /// ファイルから行を非同期で読み込む（文字列として）
    /// </summary>
    public async IAsyncEnumerable<string> ReadLinesAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fileInfo = GetFileInfo(path);
        var bufferSize = GetOptimalBufferSize(fileInfo.Length);
        
        using var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize);
        
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    /// <summary>
    /// 標準入力から行を非同期で読み込む（ReadOnlyMemoryを使用したゼロコピー処理）
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<char>> ReadStandardInputAsMemoryAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = GetStandardInput();
        
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line.AsMemory();
        }
    }

    /// <summary>
    /// 標準入力から行を非同期で読み込む（文字列として）
    /// </summary>
    public async IAsyncEnumerable<string> ReadStandardInputAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = GetStandardInput();
        
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    /// <summary>
    /// ディレクトリ内のファイルを非同期で列挙
    /// </summary>
    public async IAsyncEnumerable<string> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // 非同期コンテキストに切り替え
        
        var files = Directory.EnumerateFiles(path, searchPattern, searchOption);
        
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    /// <summary>
    /// ファイルサイズに応じた最適なバッファサイズを計算
    /// </summary>
    /// <param name="fileSize">ファイルサイズ（バイト）</param>
    /// <returns>最適なバッファサイズ</returns>
    private static int GetOptimalBufferSize(long fileSize)
    {
        // 小さなファイル（1KB未満）: 1KB
        if (fileSize < 1024)
            return 1024;
        
        // 中程度のファイル（1MB未満）: 4KB
        if (fileSize < 1024 * 1024)
            return 4096;
        
        // 大きなファイル（10MB未満）: 8KB
        if (fileSize < 10 * 1024 * 1024)
            return 8192;
        
        // 非常に大きなファイル: 16KB
        return 16384;
    }
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
