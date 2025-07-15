using System.Text;

namespace GrepCompatible.Abstractions;

/// <summary>
/// ファイルシステム操作の抽象化インターフェース
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// ファイルが存在するかどうかを確認
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <returns>ファイルが存在する場合true</returns>
    bool FileExists(string path);
    
    /// <summary>
    /// ディレクトリが存在するかどうかを確認
    /// </summary>
    /// <param name="path">ディレクトリパス</param>
    /// <returns>ディレクトリが存在する場合true</returns>
    bool DirectoryExists(string path);
    
    /// <summary>
    /// ファイル情報を取得
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <returns>ファイル情報</returns>
    IFileInfo GetFileInfo(string path);
    
    /// <summary>
    /// ファイルを開いてStreamReaderを取得
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="encoding">エンコーディング</param>
    /// <param name="bufferSize">バッファサイズ</param>
    /// <returns>StreamReader</returns>
    StreamReader OpenText(string path, Encoding encoding, int bufferSize = 4096);
    
    /// <summary>
    /// ファイルを開いてStreamを取得
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="mode">ファイルモード</param>
    /// <param name="access">アクセス権</param>
    /// <param name="share">共有権</param>
    /// <param name="bufferSize">バッファサイズ</param>
    /// <param name="options">ファイルオプション</param>
    /// <returns>Stream</returns>
    Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options);
    
    /// <summary>
    /// ディレクトリ内のファイルを列挙
    /// </summary>
    /// <param name="path">ディレクトリパス</param>
    /// <param name="searchPattern">検索パターン</param>
    /// <param name="searchOption">検索オプション</param>
    /// <returns>ファイルパスの列挙</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    
    /// <summary>
    /// 標準入力からStreamReaderを取得
    /// </summary>
    /// <returns>標準入力のStreamReader</returns>
    StreamReader GetStandardInput();
}

/// <summary>
/// ファイル情報の抽象化インターフェース
/// </summary>
public interface IFileInfo
{
    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    long Length { get; }
    
    /// <summary>
    /// ファイルが存在するかどうか
    /// </summary>
    bool Exists { get; }
    
    /// <summary>
    /// ファイルパス
    /// </summary>
    string FullName { get; }
    
    /// <summary>
    /// ファイル名
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// ディレクトリパス
    /// </summary>
    string? DirectoryName { get; }
}

/// <summary>
/// パス操作の抽象化インターフェース
/// </summary>
public interface IPath
{
    /// <summary>
    /// ディレクトリ名を取得
    /// </summary>
    /// <param name="path">パス</param>
    /// <returns>ディレクトリ名</returns>
    string? GetDirectoryName(string path);
    
    /// <summary>
    /// ファイル名を取得
    /// </summary>
    /// <param name="path">パス</param>
    /// <returns>ファイル名</returns>
    string GetFileName(string path);
    
    /// <summary>
    /// パスを結合
    /// </summary>
    /// <param name="paths">パス要素</param>
    /// <returns>結合されたパス</returns>
    string Combine(params string[] paths);
}
