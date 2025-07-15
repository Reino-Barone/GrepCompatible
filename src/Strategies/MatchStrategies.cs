using GrepCompatible.Constants;
using GrepCompatible.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace GrepCompatible.Strategies;

/// <summary>
/// 固定文字列マッチング戦略
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
        var comparison = options.GetFlagValue(OptionNames.IgnoreCase) ? 
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var currentIndex = 0;
        var patternLength = pattern.Length;
        var lineLength = line.Length;
        
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
