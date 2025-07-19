using System;

namespace GrepCompatible.Abstractions;

/// <summary>
/// ファイル検索オプション
/// </summary>
public enum SearchOption
{
    /// <summary>
    /// 最上位ディレクトリのみを検索
    /// </summary>
    TopDirectoryOnly = 0,
    
    /// <summary>
    /// 全てのサブディレクトリも含めて検索
    /// </summary>
    AllDirectories = 1
}
