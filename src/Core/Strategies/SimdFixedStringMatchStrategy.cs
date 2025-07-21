using GrepCompatible.Abstractions;
using GrepCompatible.Abstractions.Constants;


using System.Runtime.CompilerServices;

namespace GrepCompatible.Core.Strategies;

/// <summary>
/// SIMD最適化された固定文字列マッチング戦略
/// </summary>
public class SimdFixedStringMatchStrategy : IMatchStrategy
{
    public bool CanApply(IOptionContext options) => options.GetFlagValue(OptionNames.FixedStrings);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        // パターンが空の場合は早期リターン
        if (string.IsNullOrEmpty(pattern))
            yield break;
            
        // オプション値を一度だけ取得してキャッシュ
        var comparison = options.GetFlagValue(OptionNames.IgnoreCase) ? 
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        
        // SIMD最適化を使用してすべてのマッチを取得
        var matchIndices = SimdStringSearch.FindAllMatches(line.AsSpan(), pattern.AsSpan(), comparison);
        
        foreach (var matchIndex in matchIndices)
        {
            var matchedText = line.AsMemory(matchIndex, pattern.Length);
            
            yield return new MatchResult(
                fileName,
                lineNumber,
                line,
                matchedText,
                matchIndex,
                matchIndex + pattern.Length
            );
        }
    }
}

/// <summary>
/// SIMD最適化された固定文字列マッチング戦略（レガシー版との互換性のため）
/// </summary>
public class FixedStringMatchStrategySimdEnhanced : IMatchStrategy
{
    public bool CanApply(IOptionContext options) => options.GetFlagValue(OptionNames.FixedStrings);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        // パターンが空の場合は早期リターン
        if (string.IsNullOrEmpty(pattern))
            yield break;
            
        // オプション値を一度だけ取得してキャッシュ
        var comparison = options.GetFlagValue(OptionNames.IgnoreCase) ? 
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        
        var patternLength = pattern.Length;
        
        // SIMD最適化されたIndexOfを使用
        var currentIndex = 0;
        
        while (currentIndex <= line.Length - patternLength)
        {
            var remainingSpan = line.AsSpan().Slice(currentIndex);
            var foundIndex = SimdStringSearch.IndexOf(remainingSpan, pattern.AsSpan(), comparison);
            
            if (foundIndex == -1)
                break;
            
            var absoluteIndex = currentIndex + foundIndex;
            var matchedText = line.AsMemory(absoluteIndex, patternLength);
            
            yield return new MatchResult(
                fileName,
                lineNumber,
                line,
                matchedText,
                absoluteIndex,
                absoluteIndex + patternLength
            );
            
            currentIndex = absoluteIndex + 1;
        }
    }
}