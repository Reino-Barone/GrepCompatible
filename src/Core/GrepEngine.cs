using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GrepCompatible.Core;

/// <summary>
/// Grep処理のメインエンジン
/// </summary>
public interface IGrepEngine
{
    /// <summary>
    /// 検索を実行
    /// </summary>
    /// <param name="options">検索オプション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検索結果</returns>
    Task<SearchResult> SearchAsync(IDynamicOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// 並列処理対応のGrep実装
/// </summary>
public class ParallelGrepEngine(IMatchStrategyFactory strategyFactory) : IGrepEngine
{
    private readonly IMatchStrategyFactory _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));

    public async Task<SearchResult> SearchAsync(IDynamicOptions options, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var strategy = _strategyFactory.CreateStrategy(options);
        
        try
        {
            var files = await ExpandFilesAsync(options, cancellationToken);
            var results = new ConcurrentBag<FileResult>();
            
            // 並列処理でファイルを処理
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };
            
            await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
            {
                var result = await ProcessFileAsync(file, strategy, options, ct);
                results.Add(result);
            });
            
            var fileResults = results.ToArray();
            var totalMatches = fileResults.Sum(r => r.TotalMatches);
            
            stopwatch.Stop();
            
            return new SearchResult(
                fileResults,
                totalMatches,
                fileResults.Length,
                stopwatch.Elapsed
            );
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new SearchResult([], 0, 0, stopwatch.Elapsed);
        }
    }

    private async Task<IEnumerable<string>> ExpandFilesAsync(IDynamicOptions options, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var filesArg = options.GetStringListArgumentValue(ArgumentNames.Files) ?? 
            new[] { "-" }.ToList().AsReadOnly();
        
        foreach (var filePattern in filesArg)
        {
            if (filePattern == "-")
            {
                files.Add("-"); // 標準入力
                continue;
            }
            
            if (options.GetFlagValue(OptionNames.RecursiveSearch))
            {
                var expandedFiles = await ExpandRecursiveAsync(filePattern, options, cancellationToken);
                files.AddRange(expandedFiles);
            }
            else if (File.Exists(filePattern))
            {
                files.Add(filePattern);
            }
            else
            {
                // グロブパターンの展開
                var expandedFiles = ExpandGlobPattern(filePattern);
                files.AddRange(expandedFiles);
            }
        }
        
        return files.Distinct();
    }

    private Task<IEnumerable<string>> ExpandRecursiveAsync(string path, IDynamicOptions options, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        
        if (Directory.Exists(path))
        {
            var searchOption = SearchOption.AllDirectories;
            var allFiles = Directory.EnumerateFiles(path, "*", searchOption);
            
            foreach (var file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldIncludeFile(file, options))
                {
                    files.Add(file);
                }
            }
        }
        else if (File.Exists(path))
        {
            files.Add(path);
        }
        
        return Task.FromResult<IEnumerable<string>>(files);
    }

    private static IEnumerable<string> ExpandGlobPattern(string pattern)
    {
        try
        {
            var directory = Path.GetDirectoryName(pattern) ?? ".";
            var fileName = Path.GetFileName(pattern);
            
            if (Directory.Exists(directory))
            {
                return Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly);
            }
        }
        catch (Exception)
        {
            // グロブ展開に失敗した場合はパターンをそのまま返す
        }
        
        return [pattern];
    }

    private static bool ShouldIncludeFile(string filePath, IDynamicOptions options)
    {
        var fileName = Path.GetFileName(filePath);
        
        // 除外パターンのチェック
        var excludePattern = options.GetStringValue(OptionNames.ExcludePattern);
        if (excludePattern != null)
        {
            var excludeRegex = new Regex(excludePattern, RegexOptions.IgnoreCase);
            if (excludeRegex.IsMatch(fileName))
                return false;
        }
        
        // 包含パターンのチェック
        var includePattern = options.GetStringValue(OptionNames.IncludePattern);
        if (includePattern != null)
        {
            var includeRegex = new Regex(includePattern, RegexOptions.IgnoreCase);
            if (!includeRegex.IsMatch(fileName))
                return false;
        }
        
        return true;
    }

    private async Task<FileResult> ProcessFileAsync(string filePath, IMatchStrategy strategy, IDynamicOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var matches = new List<MatchResult>();
            var lineNumber = 0;
            var matchCount = 0;
            
            // 標準入力の処理
            if (filePath == "-")
            {
                return await ProcessStandardInputAsync(strategy, options, cancellationToken);
            }
            
            var pattern = options.GetStringArgumentValue(ArgumentNames.Pattern) ?? "";
            var invertMatch = options.GetFlagValue(OptionNames.InvertMatch);
            var maxCount = options.GetIntValue(OptionNames.MaxCount);
            
            // ファイルの処理（大きなファイルでもメモリ効率的）
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                
                var lineMatches = strategy.FindMatches(line, pattern, options, filePath, lineNumber);
                var lineMatchList = lineMatches.ToList();
                
                var hasMatches = lineMatchList.Count > 0;
                
                // 反転マッチの処理
                if (invertMatch)
                {
                    hasMatches = !hasMatches;
                    if (hasMatches)
                    {
                        // 反転マッチの場合は行全体をマッチとする
                        matches.Add(new MatchResult(filePath, lineNumber, line, line.AsMemory(), 0, line.Length));
                        matchCount++;
                    }
                }
                else if (hasMatches)
                {
                    matches.AddRange(lineMatchList);
                    matchCount += lineMatchList.Count;
                }
                
                // 最大マッチ数の制限
                if (maxCount.HasValue && matchCount >= maxCount.Value)
                    break;
            }
            
            return new FileResult(filePath, matches.AsReadOnly(), matchCount);
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
    }

    private async Task<FileResult> ProcessStandardInputAsync(IMatchStrategy strategy, IDynamicOptions options, CancellationToken cancellationToken)
    {
        var matches = new List<MatchResult>();
        var lineNumber = 0;
        var matchCount = 0;
        const string fileName = "(standard input)";
        
        var pattern = options.GetStringArgumentValue(ArgumentNames.Pattern) ?? "";
        var invertMatch = options.GetFlagValue(OptionNames.InvertMatch);
        var maxCount = options.GetIntValue(OptionNames.MaxCount);
        
        try
        {
            string? line;
            while ((line = await Console.In.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                
                var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                var lineMatchList = lineMatches.ToList();
                
                var hasMatches = lineMatchList.Count > 0;
                
                if (invertMatch)
                {
                    hasMatches = !hasMatches;
                    if (hasMatches)
                    {
                        matches.Add(new MatchResult(fileName, lineNumber, line, line.AsMemory(), 0, line.Length));
                        matchCount++;
                    }
                }
                else if (hasMatches)
                {
                    matches.AddRange(lineMatchList);
                    matchCount += lineMatchList.Count;
                }
                
                if (maxCount.HasValue && matchCount >= maxCount.Value)
                    break;
            }
            
            return new FileResult(fileName, matches.AsReadOnly(), matchCount);
        }
        catch (Exception ex)
        {
            return new FileResult(fileName, [], 0, true, ex.Message);
        }
    }
}
