using GrepCompatible.Models;

namespace GrepCompatible.Abstractions;

/// <summary>
/// ファイル探索とパターンマッチングを提供するサービス
/// </summary>
public interface IFileSearchService
{
    /// <summary>
    /// ファイルパターンを展開して実際のファイルリストを取得
    /// </summary>
    /// <param name="options">検索オプション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>展開されたファイルパスのリスト</returns>
    Task<IEnumerable<string>> ExpandFilesAsync(IOptionContext options, CancellationToken cancellationToken = default);

    /// <summary>
    /// ファイルが包含・除外パターンに基づいて処理対象に含まれるべきかを判定
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="options">検索オプション</param>
    /// <returns>処理対象に含まれる場合はtrue</returns>
    bool ShouldIncludeFile(string filePath, IOptionContext options);
}
