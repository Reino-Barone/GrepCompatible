using System.Text;
using GrepCompatible.Abstractions;
using System.Runtime.CompilerServices;
using System.IO.Enumeration;

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
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, System.IO.SearchOption searchOption)
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
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    /// <summary>
    /// ディレクトリ内のファイルを効率的に非同期で列挙（FileSystemEnumerableを使用）
    /// </summary>
    public async IAsyncEnumerable<string> EnumerateFilesAsync(string path, string searchPattern, System.IO.SearchOption searchOption, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // まずディレクトリの存在確認
        if (!Directory.Exists(path))
            yield break;
        
        // FileSystemEnumerableを使用した高性能な実装
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = searchOption == System.IO.SearchOption.AllDirectories,
            MatchType = MatchType.Simple,
            MatchCasing = MatchCasing.PlatformDefault,
            IgnoreInaccessible = true, // アクセス権限のないファイルを自動的にスキップ
            BufferSize = 16384 // 16KB バッファ
        };
        
        // 効率的なバッチ処理
        const int batchSize = 50;
        var batch = new List<string>(batchSize);
        
        FileSystemEnumerable<string>? enumerable = null;
        try
        {
            // FileSystemEnumerableを使用してFileSystemEntryから文字列パスに変換
            enumerable = new FileSystemEnumerable<string>(
                path, 
                (ref FileSystemEntry entry) => entry.ToFullPath(), 
                enumerationOptions)
            {
                // カスタムフィルタリング: ファイルのみを対象とし、パターンマッチングを適用
                ShouldIncludePredicate = (ref FileSystemEntry entry) => 
                {
                    if (entry.IsDirectory)
                        return false;
                    
                    // シンプルなワイルドカードマッチング
                    return MatchesPattern(entry.FileName, searchPattern);
                }
            };
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
        {
            yield break; // エラーの場合は終了
        }
        
        if (enumerable == null)
            yield break;
        
        foreach (var file in enumerable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(file);
            
            // バッチが満杯になったら非同期的に処理を譲る
            if (batch.Count >= batchSize)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                
                foreach (var batchFile in batch)
                {
                    yield return batchFile;
                }
                
                batch.Clear();
            }
        }
        
        // 残りのファイルを処理
        foreach (var file in batch)
        {
            yield return file;
        }
    }
    
    /// <summary>
    /// シンプルなワイルドカードパターンマッチング
    /// </summary>
    private static bool MatchesPattern(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pattern)
    {
        // "*" は全てマッチ
        if (pattern.Length == 1 && pattern[0] == '*')
            return true;
        
        // 単純なパターンマッチング（例：*.txt）
        if (pattern.StartsWith("*") && pattern.Length > 1)
        {
            var extension = pattern[1..]; // "*" を除去
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }
        
        // 正確なマッチング
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// より効率的な非同期ファイル列挙（チャンク化処理）
    /// </summary>
    public async IAsyncEnumerable<string> EnumerateFilesAsyncChunked(string path, string searchPattern, System.IO.SearchOption searchOption, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int chunkSize = 100; // 100ファイルずつ処理
        
        var enumerable = Directory.EnumerateFiles(path, searchPattern, searchOption);
        var chunk = new List<string>(chunkSize);
        
        foreach (var file in enumerable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunk.Add(file);
            
            if (chunk.Count >= chunkSize)
            {
                // チャンクが満杯になったら非同期的に処理を譲る
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                
                foreach (var chunkFile in chunk)
                {
                    yield return chunkFile;
                }
                
                chunk.Clear();
            }
        }
        
        // 残りのファイルを処理
        foreach (var file in chunk)
        {
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
