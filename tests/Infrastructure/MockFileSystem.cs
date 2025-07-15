using System.Text;
using GrepCompatible.Abstractions;

namespace GrepCompatible.Test.Infrastructure;

/// <summary>
/// テスト用のモックファイルシステム
/// </summary>
/// <remarks>
/// このクラスは実行環境に依存しない統一されたパス処理を提供します。
/// テストでは常に'/'区切りを使用し、実行プラットフォームに関係なく一貫した動作を保証します。
/// </remarks>
public class MockFileSystem : IFileSystem
{
    private readonly Dictionary<string, MockFileInfo> _files = new();
    private readonly HashSet<string> _directories = new();
    private string? _standardInputContent;
    
    /// <summary>
    /// 標準入力の内容を設定
    /// </summary>
    /// <param name="content">標準入力の内容</param>
    public void SetStandardInput(string? content)
    {
        _standardInputContent = content;
    }
    
    /// <summary>
    /// パスを正規化する（テスト用統一フォーマット）
    /// </summary>
    /// <param name="path">パス</param>
    /// <returns>正規化されたパス</returns>
    /// <remarks>
    /// テスト環境では常に'/'区切りに統一し、実行環境による違いを排除します。
    /// 実際のファイルシステムは使用せず、純粋に文字列操作のみで処理します。
    /// 
    /// 【設計理念】
    /// - 実行環境（Windows/Linux/macOS）に依存しない
    /// - System.IO.Path を使用しない（実行環境依存を避けるため）
    /// - テスト用の仮想ファイルシステムとして動作
    /// - 一貫した文字列比較を保証
    /// </remarks>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        
        // テスト環境では常に'/'区切りに統一
        // System.IO.Path.GetFullPath等は使用しない（実行環境依存を避けるため）
        return path.Replace('\\', '/').Replace("//", "/").TrimEnd('/');
    }
    
    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));
    
    public bool DirectoryExists(string path) => _directories.Contains(NormalizePath(path));
    
    public IFileInfo GetFileInfo(string path) 
    {
        var normalizedPath = NormalizePath(path);
        return _files.TryGetValue(normalizedPath, out var fileInfo) ? fileInfo : new MockFileInfo(path, false);
    }
    
    public StreamReader OpenText(string path, Encoding encoding, int bufferSize = 4096)
    {
        var normalizedPath = NormalizePath(path);
        if (!_files.TryGetValue(normalizedPath, out var fileInfo))
            throw new FileNotFoundException($"File not found: {path}");
        
        var stream = new MemoryStream(encoding.GetBytes(fileInfo.Content));
        return new StreamReader(stream, encoding);
    }
    
    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        var normalizedPath = NormalizePath(path);
        if (!_files.TryGetValue(normalizedPath, out var fileInfo))
            throw new FileNotFoundException($"File not found: {path}");
        
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(fileInfo.Content));
        
        // FileStreamを模倣するため、MemoryStreamを継承したクラスを作成
        return new MockFileStream(stream);
    }
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(path);
        if (!_directories.Contains(normalizedPath))
            yield break;
        
        var pattern = ConvertGlobToRegex(searchPattern);
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        foreach (var filePath in _files.Keys)
        {
            var fileName = GetFileNameFromPath(filePath);
            var fileDirectory = GetDirectoryFromPath(filePath);
            
            if (searchOption == SearchOption.AllDirectories)
            {
                if (IsPathUnderDirectory(fileDirectory, normalizedPath) && regex.IsMatch(fileName))
                    yield return filePath;
            }
            else
            {
                if (string.Equals(fileDirectory, normalizedPath, StringComparison.OrdinalIgnoreCase) && regex.IsMatch(fileName))
                    yield return filePath;
            }
        }
    }
    
    /// <summary>
    /// パスからファイル名を取得（環境非依存）
    /// </summary>
    private static string GetFileNameFromPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        var lastSlashIndex = normalizedPath.LastIndexOf('/');
        return lastSlashIndex >= 0 ? normalizedPath.Substring(lastSlashIndex + 1) : normalizedPath;
    }
    
    /// <summary>
    /// パスからディレクトリ部分を取得（環境非依存）
    /// </summary>
    private static string GetDirectoryFromPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        var lastSlashIndex = normalizedPath.LastIndexOf('/');
        return lastSlashIndex >= 0 ? normalizedPath.Substring(0, lastSlashIndex) : ".";
    }
    
    /// <summary>
    /// 指定されたパスが特定のディレクトリ以下にあるかどうかを判定
    /// </summary>
    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        if (string.Equals(filePath, directoryPath, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return filePath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// ファイルを追加
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="content">ファイル内容</param>
    public void AddFile(string path, string content)
    {
        var normalizedPath = NormalizePath(path);
        var directory = GetDirectoryFromPath(normalizedPath);
        
        if (!string.IsNullOrEmpty(directory) && directory != ".")
            AddDirectory(directory);
        
        _files[normalizedPath] = new MockFileInfo(normalizedPath, true, content);
    }
    
    /// <summary>
    /// ディレクトリを追加
    /// </summary>
    /// <param name="path">ディレクトリパス</param>
    public void AddDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        _directories.Add(normalizedPath);
        
        // 親ディレクトリも追加
        var parent = GetDirectoryFromPath(normalizedPath);
        if (!string.IsNullOrEmpty(parent) && parent != "." && !_directories.Contains(parent))
            AddDirectory(parent);
    }
    
    /// <summary>
    /// ファイルを削除
    /// </summary>
    /// <param name="path">ファイルパス</param>
    public void RemoveFile(string path)
    {
        _files.Remove(NormalizePath(path));
    }
    
    /// <summary>
    /// ディレクトリを削除
    /// </summary>
    /// <param name="path">ディレクトリパス</param>
    public void RemoveDirectory(string path)
    {
        _directories.Remove(NormalizePath(path));
    }
    
    /// <summary>
    /// すべてのファイルとディレクトリをクリア
    /// </summary>
    public void Clear()
    {
        _files.Clear();
        _directories.Clear();
    }
    
    private static string ConvertGlobToRegex(string glob)
    {
        // 簡易的なグロブパターンから正規表現への変換
        return glob.Replace("*", ".*").Replace("?", ".");
    }
    
    public StreamReader GetStandardInput()
    {
        var content = _standardInputContent ?? "";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new StreamReader(stream, Encoding.UTF8);
    }
}

/// <summary>
/// テスト用のモックファイル情報
/// </summary>
public class MockFileInfo(string path, bool exists, string content = "") : IFileInfo
{
    public long Length => Encoding.UTF8.GetByteCount(content);
    public bool Exists => exists;
    public string FullName => path;
    public string Name => Path.GetFileName(path);
    public string? DirectoryName => Path.GetDirectoryName(path);
    public string Content => content;
}

/// <summary>
/// テスト用のモックFileStream
/// </summary>
public class MockFileStream : Stream
{
    private readonly MemoryStream _innerStream;
    private bool _disposed = false;
    
    public MockFileStream(MemoryStream innerStream)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
    }
    
    public override bool CanRead => !_disposed && _innerStream.CanRead;
    public override bool CanSeek => !_disposed && _innerStream.CanSeek;
    public override bool CanWrite => !_disposed && _innerStream.CanWrite;
    public override long Length => _disposed ? throw new ObjectDisposedException(nameof(MockFileStream)) : _innerStream.Length;
    public override long Position 
    { 
        get => _disposed ? throw new ObjectDisposedException(nameof(MockFileStream)) : _innerStream.Position; 
        set => _innerStream.Position = _disposed ? throw new ObjectDisposedException(nameof(MockFileStream)) : value; 
    }
    
    public override void Flush() => _innerStream.Flush();
    
    public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
    
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    
    public override void SetLength(long value) => _innerStream.SetLength(value);
    
    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _innerStream.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// テスト用のモックパスヘルパー
/// </summary>
public class MockPathHelper : IPath
{
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    
    public string GetFileName(string path) => Path.GetFileName(path);
    
    public string Combine(params string[] paths) => Path.Combine(paths);
}
