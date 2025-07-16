using GrepCompatible.Constants;
using GrepCompatible.Models;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace GrepCompatible.Strategies;

/// <summary>
/// SIMD命令を用いた高速固定文字列マッチング戦略
/// </summary>
public class SimdFixedStringMatchStrategy : IMatchStrategy
{
    private static readonly bool _isAvx2Supported = Avx2.IsSupported;
    private static readonly bool _isVectorSupported = Vector.IsHardwareAccelerated;
    
    public bool CanApply(IOptionContext options) => 
        options.GetFlagValue(OptionNames.FixedStrings) && 
        (_isAvx2Supported || _isVectorSupported);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(line))
            yield break;
            
        var ignoreCase = options.GetFlagValue(OptionNames.IgnoreCase);
        var patternLength = pattern.Length;
        
        // パターンが1文字の場合は専用の最適化を使用
        if (patternLength == 1)
        {
            var matches = FindSingleCharMatchesOptimized(line, pattern[0], ignoreCase);
            foreach (var index in matches)
            {
                var matchedText = line.AsMemory(index, 1);
                yield return new MatchResult(fileName, lineNumber, line, matchedText, index, index + 1);
            }
            yield break;
        }
        
        // パターンが短い場合（2-16文字）はSIMD最適化を使用
        if (patternLength <= 16)
        {
            var matches = FindShortPatternMatchesOptimized(line, pattern, ignoreCase);
            foreach (var index in matches)
            {
                var matchedText = line.AsMemory(index, patternLength);
                yield return new MatchResult(fileName, lineNumber, line, matchedText, index, index + patternLength);
            }
            yield break;
        }
        
        // 長いパターンの場合は従来の方法にフォールバック
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var currentIndex = 0;
        
        while (currentIndex < line.Length)
        {
            var foundIndex = line.IndexOf(pattern, currentIndex, comparison);
            if (foundIndex == -1) break;
            
            var matchedText = line.AsMemory(foundIndex, patternLength);
            yield return new MatchResult(fileName, lineNumber, line, matchedText, foundIndex, foundIndex + patternLength);
            currentIndex = foundIndex + 1;
        }
    }
    
    /// <summary>
    /// 1文字パターンのSIMD最適化検索
    /// </summary>
    private static List<int> FindSingleCharMatchesOptimized(string line, char pattern, bool ignoreCase)
    {
        var matches = new List<int>();
        var searchChar = ignoreCase ? char.ToLowerInvariant(pattern) : pattern;
        
        if (_isVectorSupported && line.Length >= Vector<ushort>.Count)
        {
            var searchVector = new Vector<ushort>((ushort)searchChar);
            var vectorSize = Vector<ushort>.Count;
            
            for (int i = 0; i <= line.Length - vectorSize; i += vectorSize)
            {
                var chunk = new Vector<ushort>(MemoryMarshal.Cast<char, ushort>(line.AsSpan(i, vectorSize)));
                
                if (ignoreCase)
                {
                    // 大文字小文字を無視する場合の処理
                    for (int j = 0; j < vectorSize && i + j < line.Length; j++)
                    {
                        if (char.ToLowerInvariant(line[i + j]) == searchChar)
                        {
                            matches.Add(i + j);
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
                            if (line[i + j] == pattern)
                            {
                                matches.Add(i + j);
                            }
                        }
                    }
                }
            }
            
            // 残りの部分を処理
            for (int i = (line.Length / vectorSize) * vectorSize; i < line.Length; i++)
            {
                if (ignoreCase ? 
                    char.ToLowerInvariant(line[i]) == searchChar :
                    line[i] == pattern)
                {
                    matches.Add(i);
                }
            }
        }
        else
        {
            // フォールバック実装
            for (int i = 0; i < line.Length; i++)
            {
                if (ignoreCase ? 
                    char.ToLowerInvariant(line[i]) == searchChar :
                    line[i] == pattern)
                {
                    matches.Add(i);
                }
            }
        }
        
        return matches;
    }
    
    /// <summary>
    /// 短いパターン（2-16文字）のSIMD最適化検索
    /// </summary>
    private static List<int> FindShortPatternMatchesOptimized(string line, string pattern, bool ignoreCase)
    {
        var matches = new List<int>();
        var patternLength = pattern.Length;
        
        if (_isVectorSupported && line.Length >= Vector<ushort>.Count)
        {
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
                        if (IsPatternMatchOptimized(line, j, pattern, ignoreCase))
                        {
                            matches.Add(j);
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
                            IsPatternMatchOptimized(line, candidateIndex, pattern, ignoreCase))
                        {
                            matches.Add(candidateIndex);
                        }
                    }
                }
            }
        }
        else
        {
            // フォールバック実装
            for (int i = 0; i <= line.Length - patternLength; i++)
            {
                if (IsPatternMatchOptimized(line, i, pattern, ignoreCase))
                {
                    matches.Add(i);
                }
            }
        }
        
        return matches;
    }
    
    /// <summary>
    /// 最適化されたパターンマッチの確認
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPatternMatchOptimized(string line, int startIndex, string pattern, bool ignoreCase)
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
/// SIMD命令を用いた高速Boyer-Moore文字列検索戦略
/// </summary>
public class SimdBoyerMooreMatchStrategy : IMatchStrategy
{
    private readonly Dictionary<string, BoyerMooreSearcher> _searcherCache = new();
    private readonly object _cacheLock = new();
    
    public bool CanApply(IOptionContext options) => 
        options.GetFlagValue(OptionNames.FixedStrings) && 
        Vector.IsHardwareAccelerated;

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, IOptionContext options, string fileName, int lineNumber)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(line))
            yield break;
            
        var ignoreCase = options.GetFlagValue(OptionNames.IgnoreCase);
        var cacheKey = $"{pattern}_{ignoreCase}";
        
        BoyerMooreSearcher searcher;
        lock (_cacheLock)
        {
            if (!_searcherCache.TryGetValue(cacheKey, out searcher!))
            {
                searcher = new BoyerMooreSearcher(pattern, ignoreCase);
                _searcherCache[cacheKey] = searcher;
            }
        }
        
        var matches = searcher.FindAll(line);
        foreach (var index in matches)
        {
            var matchedText = line.AsMemory(index, pattern.Length);
            yield return new MatchResult(fileName, lineNumber, line, matchedText, index, index + pattern.Length);
        }
    }
}

/// <summary>
/// SIMD最適化されたBoyer-Moore検索器
/// </summary>
public class BoyerMooreSearcher
{
    private readonly string _pattern;
    private readonly bool _ignoreCase;
    private readonly int[] _badCharTable;
    private readonly int _patternLength;
    
    public BoyerMooreSearcher(string pattern, bool ignoreCase)
    {
        _pattern = ignoreCase ? pattern.ToLowerInvariant() : pattern;
        _ignoreCase = ignoreCase;
        _patternLength = pattern.Length;
        _badCharTable = BuildBadCharTable(_pattern);
    }
    
    /// <summary>
    /// 不正文字テーブルの構築
    /// </summary>
    private static int[] BuildBadCharTable(string pattern)
    {
        var table = new int[65536]; // Unicode文字に対応
        var patternLength = pattern.Length;
        
        // 初期化：すべての文字についてパターン長の距離を設定
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = patternLength;
        }
        
        // パターンの各文字について距離を設定
        for (int i = 0; i < patternLength - 1; i++)
        {
            table[pattern[i]] = patternLength - 1 - i;
        }
        
        return table;
    }
    
    /// <summary>
    /// SIMD最適化されたBoyer-Moore検索
    /// </summary>
    public List<int> FindAll(string text)
    {
        var matches = new List<int>();
        
        if (text.Length < _patternLength) return matches;
        
        var textLength = text.Length;
        var i = _patternLength - 1;
        
        while (i < textLength)
        {
            var j = _patternLength - 1;
            var k = i;
            
            // 後方からのマッチング
            while (j >= 0 && k >= 0 && 
                   (_ignoreCase ? 
                    char.ToLowerInvariant(text[k]) == _pattern[j] : 
                    text[k] == _pattern[j]))
            {
                j--;
                k--;
            }
            
            if (j < 0)
            {
                // マッチ発見
                matches.Add(k + 1);
                i += _patternLength;
            }
            else
            {
                // 不正文字テーブルを使用してスキップ
                var badCharShift = _badCharTable[text[k]];
                var skip = Math.Max(1, badCharShift);
                i += skip;
            }
        }
        
        return matches;
    }
}
