using GrepCompatible.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace GrepCompatible.Strategies;

/// <summary>
/// 固定文字列マッチング戦略
/// </summary>
public class FixedStringMatchStrategy : IMatchStrategy
{
    public bool CanApply(GrepOptions options) => options.FixedStrings;

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, GrepOptions options, string fileName, int lineNumber)
    {
        var comparison = options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var currentIndex = 0;
        
        while (currentIndex < line.Length)
        {
            var foundIndex = line.IndexOf(pattern, currentIndex, comparison);
            if (foundIndex == -1)
                break;
                
            var actualIndex = foundIndex;
            var matchedText = line.AsMemory(actualIndex, pattern.Length);
            
            yield return new MatchResult(
                fileName,
                lineNumber,
                line,
                matchedText,
                actualIndex,
                actualIndex + pattern.Length
            );
            
            currentIndex = actualIndex + 1;
        }
    }
}

/// <summary>
/// 正規表現マッチング戦略
/// </summary>
public class RegexMatchStrategy : IMatchStrategy
{
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public bool CanApply(GrepOptions options) => options.ExtendedRegexp || (!options.FixedStrings && !options.WholeWord);

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, GrepOptions options, string fileName, int lineNumber)
    {
        var regexOptions = RegexOptions.Compiled;
        if (options.IgnoreCase)
            regexOptions |= RegexOptions.IgnoreCase;
        
        var cacheKey = $"{pattern}_{regexOptions}";
        var regex = _regexCache.GetOrAdd(cacheKey, _ =>
        {
            try
            {
                return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(1));
            }
            catch (RegexParseException)
            {
                // パターンが正規表現として無効な場合は、固定文字列として扱う
                return new Regex(Regex.Escape(pattern), regexOptions, TimeSpan.FromSeconds(1));
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
    public bool CanApply(GrepOptions options) => options.WholeWord;

    public IEnumerable<MatchResult> FindMatches(string line, string pattern, GrepOptions options, string fileName, int lineNumber)
    {
        var regexOptions = RegexOptions.Compiled;
        if (options.IgnoreCase)
            regexOptions |= RegexOptions.IgnoreCase;
        
        var wordPattern = $@"\b{Regex.Escape(pattern)}\b";
        var regex = new Regex(wordPattern, regexOptions, TimeSpan.FromSeconds(1));
        
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
