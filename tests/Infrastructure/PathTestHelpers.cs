using System;
using System.Runtime.InteropServices;

namespace GrepCompatible.Tests.Infrastructure;

/// <summary>
/// パステストヘルパー - クロスプラットフォーム対応
/// </summary>
public static class PathTestHelpers
{
    /// <summary>
    /// テスト用の相対パスを作成（実行環境に依存しない）
    /// </summary>
    /// <param name="parts">パス部分</param>
    /// <returns>相対パス</returns>
    public static string CreateRelativePath(params string[] parts)
    {
        // テスト環境では常に統一された区切り文字を使用
        return string.Join("/", parts);
    }
    
    /// <summary>
    /// テスト用の絶対パスを作成（実行環境に依存しない）
    /// </summary>
    /// <param name="parts">パス部分</param>
    /// <returns>絶対パス</returns>
    public static string CreateAbsolutePath(params string[] parts)
    {
        // テスト環境では常に統一された区切り文字を使用
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "C:/" + string.Join("/", parts);
        else
            return "/" + string.Join("/", parts);
    }
    
    /// <summary>
    /// テスト用のディレクトリパスを作成（実行環境に依存しない）
    /// </summary>
    /// <param name="parts">パス部分</param>
    /// <returns>ディレクトリパス</returns>
    public static string CreateDirectoryPath(params string[] parts)
    {
        return string.Join("/", parts);
    }
    
    /// <summary>
    /// テスト用のファイルパスを作成（実行環境に依存しない）
    /// </summary>
    /// <param name="directory">ディレクトリ</param>
    /// <param name="fileName">ファイル名</param>
    /// <returns>ファイルパス</returns>
    public static string CreateFilePath(string directory, string fileName)
    {
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }
    
    /// <summary>
    /// パスの比較（テスト用）
    /// </summary>
    /// <param name="path1">パス1</param>
    /// <param name="path2">パス2</param>
    /// <returns>同じパスの場合true</returns>
    public static bool ArePathsEqual(string path1, string path2)
    {
        return NormalizePath(path1).Equals(NormalizePath(path2), StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// パスの正規化（テスト用）
    /// </summary>
    /// <param name="path">パス</param>
    /// <returns>正規化されたパス</returns>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        
        return path.Replace('\\', '/').TrimEnd('/');
    }
}
