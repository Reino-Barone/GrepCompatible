using GrepCompatible.Constants;
using GrepCompatible.Models;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GrepCompatible.Strategies;

/// <summary>
/// 固定文字列マッチング戦略（SIMD最適化）
/// </summary>
public class FixedStringMatchStrategy : IMatchStrategy
{
    public bool CanApply(IOptionContext options) => options.GetFlagValue(OptionNames.FixedStrings);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        // パターンが空の場合は早期リターン
        if (string.IsNullOrEmpty(pattern))
            yield break;
            
        // オプション値を一度だけ取得してキャッシュ
        var ignoreCase = options.GetFlagValue(OptionNames.IgnoreCase);
        var patternLength = pattern.Length;
        var lineLength = line.Length;
        
        // SIMD最適化を使用可能かチェック
        if (Vector.IsHardwareAccelerated && patternLength <= 16)
        {
            // SIMD最適化パス
            foreach (var match in FindMatchesWithSIMD(line, pattern, ignoreCase, fileName, lineNumber))
                yield return match;
        }
        else
        {
            // 従来の最適化パス
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var currentIndex = 0;
            
            while (currentIndex < lineLength)
            {
                var foundIndex = line.IndexOf(pattern, currentIndex, comparison);
                if (foundIndex == -1)
                    break;
                    
                var matchedText = line.AsMemory(foundIndex, patternLength);
                
                yield return new MatchResult(
                    fileName,
                    lineNumber,
                    line,
                    matchedText,
                    foundIndex,
                    foundIndex + patternLength
                );
                
                currentIndex = foundIndex + 1;
            }
        }
    }
    
    /// <summary>
    /// SIMD最適化されたマッチング処理
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IEnumerable<MatchResult> FindMatchesWithSIMD(string line, string pattern, bool ignoreCase, string fileName, int lineNumber)
    {
        var patternLength = pattern.Length;
        
        if (patternLength == 1)
        {
            // 1文字の場合の最適化
            var searchChar = ignoreCase ? char.ToLowerInvariant(pattern[0]) : pattern[0];
            var searchVector = new Vector<ushort>((ushort)searchChar);
            var vectorSize = Vector<ushort>.Count;
            
            for (int i = 0; i <= line.Length - vectorSize; i += vectorSize)
            {
                var remainingLength = Math.Min(vectorSize, line.Length - i);
                if (remainingLength < vectorSize)
                {
                    // 残りの部分を個別に処理
                    for (int j = i; j < line.Length; j++)
                    {
                        if (ignoreCase ? 
                            char.ToLowerInvariant(line[j]) == searchChar :
                            line[j] == pattern[0])
                        {
                            var matchedText = line.AsMemory(j, 1);
                            yield return new MatchResult(fileName, lineNumber, line, matchedText, j, j + 1);
                        }
                    }
                    break;
                }
                
                var chunk = new Vector<ushort>(MemoryMarshal.Cast<char, ushort>(line.AsSpan(i, vectorSize)));
                
                if (ignoreCase)
                {
                    // 大文字小文字を無視する場合の処理
                    for (int j = 0; j < vectorSize && i + j < line.Length; j++)
                    {
                        if (char.ToLowerInvariant(line[i + j]) == searchChar)
                        {
                            var matchedText = line.AsMemory(i + j, 1);
                            yield return new MatchResult(fileName, lineNumber, line, matchedText, i + j, i + j + 1);
                        }
                    }
                }
                else
                {
                    var comparison = Vector.Equals(chunk, searchVector);
                    if (Vector.EqualsAny(comparison, Vector<ushort>.AllBitsSet))
                    {
                        for (int j = 0; j < vectorSize && i + j < line.Length; j++)
                        {
                            if (line[i + j] == pattern[0])
                            {
                                var matchedText = line.AsMemory(i + j, 1);
                                yield return new MatchResult(fileName, lineNumber, line, matchedText, i + j, i + j + 1);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // 複数文字の場合の最適化
            var firstChar = ignoreCase ? char.ToLowerInvariant(pattern[0]) : pattern[0];
            var firstCharVector = new Vector<ushort>((ushort)firstChar);
            var vectorSize = Vector<ushort>.Count;
            
            for (int i = 0; i <= line.Length - patternLength; i += vectorSize)
            {
                var remainingLength = Math.Min(vectorSize, line.Length - i);
                if (remainingLength < vectorSize)
                {
                    // 残りの部分を個別に処理
                    for (int j = i; j <= line.Length - patternLength; j++)
                    {
                        if (IsPatternMatch(line, j, pattern, ignoreCase))
                        {
                            var matchedText = line.AsMemory(j, patternLength);
                            yield return new MatchResult(fileName, lineNumber, line, matchedText, j, j + patternLength);
                        }
                    }
                    break;
                }
                
                var chunk = new Vector<ushort>(MemoryMarshal.Cast<char, ushort>(line.AsSpan(i, vectorSize)));
                var comparison = Vector.Equals(chunk, firstCharVector);
                
                if (Vector.EqualsAny(comparison, Vector<ushort>.AllBitsSet))
                {
                    // 候補位置で完全なパターンマッチを確認
                    for (int j = 0; j < vectorSize; j++)
                    {
                        var candidateIndex = i + j;
                        if (candidateIndex <= line.Length - patternLength &&
                            IsPatternMatch(line, candidateIndex, pattern, ignoreCase))
                        {
                            var matchedText = line.AsMemory(candidateIndex, patternLength);
                            yield return new MatchResult(fileName, lineNumber, line, matchedText, candidateIndex, candidateIndex + patternLength);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// パターンマッチの確認
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPatternMatch(string line, int startIndex, string pattern, bool ignoreCase)
    {
        if (startIndex + pattern.Length > line.Length) return false;
        
        if (ignoreCase)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (char.ToLowerInvariant(line[startIndex + i]) != char.ToLowerInvariant(pattern[i]))
                    return false;
            }
        }
        else
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (line[startIndex + i] != pattern[i])
                    return false;
            }
        }
        
        return true;
    }
}

/// <summary>
/// 正規表現マッチング戦略
/// </summary>
public class RegexMatchStrategy : IMatchStrategy
{
    private readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> _regexCache = new();

    public bool CanApply(IOptionContext options) => 
        options.GetFlagValue(OptionNames.ExtendedRegexp) || 
        (!options.GetFlagValue(OptionNames.FixedStrings) && 
         !options.GetFlagValue(OptionNames.WholeWord));

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        // パターンが空の場合は早期リターン
        if (string.IsNullOrEmpty(pattern))
            yield break;
            
        var regexOptions = RegexOptions.Compiled;
        if (options.GetFlagValue(OptionNames.IgnoreCase))
            regexOptions |= RegexOptions.IgnoreCase;
        
        var cacheKey = (pattern, regexOptions);
        var regex = _regexCache.GetOrAdd(cacheKey, key =>
        {
            try
            {
                return new Regex(key.Pattern, key.Options, TimeSpan.FromSeconds(1));
            }
            catch (RegexParseException)
            {
                // パターンが正規表現として無効な場合は、固定文字列として扱う
                return new Regex(Regex.Escape(key.Pattern), key.Options, TimeSpan.FromSeconds(1));
            }
        });

        var matches = regex.Matches(line);
        
        foreach (Match match in matches)
        {
            var matchedText = line.AsMemory(match.Index, match.Length);
            yield return new MatchResult(
                fileName,
                lineNumber,
                line,
                matchedText,
                match.Index,
                match.Index + match.Length
            );
        }
    }
}

/// <summary>
/// 単語境界マッチング戦略
/// </summary>
public class WholeWordMatchStrategy : IMatchStrategy
{
    private readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> _regexCache = new();

    public bool CanApply(IOptionContext options) => options.GetFlagValue(OptionNames.WholeWord);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        // パターンが空の場合は早期リターン
        if (string.IsNullOrEmpty(pattern))
            yield break;
            
        var regexOptions = RegexOptions.Compiled;
        if (options.GetFlagValue(OptionNames.IgnoreCase))
            regexOptions |= RegexOptions.IgnoreCase;
        
        var wordPattern = $@"\b{Regex.Escape(pattern)}\b";
        var cacheKey = (wordPattern, regexOptions);
        
        var regex = _regexCache.GetOrAdd(cacheKey, key =>
            new Regex(key.Pattern, key.Options, TimeSpan.FromSeconds(1)));
        
        var matches = regex.Matches(line);
        
        foreach (Match match in matches)
        {
            var matchedText = line.AsMemory(match.Index, match.Length);
            yield return new MatchResult(
                fileName,
                lineNumber,
                line,
                matchedText,
                match.Index,
                match.Index + match.Length
            );
        }
    }
}
