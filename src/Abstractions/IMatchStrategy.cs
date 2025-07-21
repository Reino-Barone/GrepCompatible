using GrepCompatible.Core;

namespace GrepCompatible.Abstractions;

/// <summary>
/// マッチング戦略のインターフェース
/// </summary>
public interface IMatchStrategy
{
    /// <summary>
    /// 指定された行がパターンにマッチするかどうかを判定
    /// </summary>
    /// <param name="line">検索対象の行</param>
    /// <param name="pattern">検索パターン</param>
    /// <param name="options">検索オプション</param>
    /// <returns>マッチした場合はマッチ情報、そうでなければnull</returns>
    IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber);
    
    /// <summary>
    /// この戦略が指定されたオプションに適用可能かどうかを判定
    /// </summary>
    /// <param name="options">検索オプション</param>
    /// <returns>適用可能な場合はtrue</returns>
    bool CanApply(IOptionContext options);
}

/// <summary>
/// マッチング戦略のファクトリーインターフェース
/// </summary>
public interface IMatchStrategyFactory
{
    /// <summary>
    /// 指定されたオプションに適した戦略を作成
    /// </summary>
    /// <param name="options">検索オプション</param>
    /// <returns>適切な戦略</returns>
    IMatchStrategy CreateStrategy(IOptionContext options);
    
    /// <summary>
    /// 新しい戦略を登録
    /// </summary>
    /// <param name="strategy">登録する戦略</param>
    void RegisterStrategy(IMatchStrategy strategy);
}