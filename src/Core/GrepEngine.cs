using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Buffers;
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
    Task<SearchResult> SearchAsync(IOptionContext options, CancellationToken cancellationToken = default);
}

/// <summary>
/// 並列処理対応のGrep実装
/// </summary>
public class ParallelGrepEngine(IMatchStrategyFactory strategyFactory, IFileSystem fileSystem, IPath pathHelper) : IGrepEngine
{
    private readonly IMatchStrategyFactory _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IPath _pathHelper = pathHelper ?? throw new ArgumentNullException(nameof(pathHelper));
    private static readonly string[] sourceArray = ["-"];
    private static readonly ArrayPool<MatchResult> _matchPool = ArrayPool<MatchResult>.Shared;

    public async Task<SearchResult> SearchAsync(IOptionContext options, CancellationToken cancellationToken = default)
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

    private async Task<IEnumerable<string>> ExpandFilesAsync(IOptionContext options, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var filesArg = options.GetStringListArgumentValue(ArgumentNames.Files) ??
            sourceArray.ToList().AsReadOnly();
        var isRecursive = options.GetFlagValue(OptionNames.RecursiveSearch);
        
        foreach (var filePattern in filesArg)
        {
            if (filePattern == "-")
            {
                files.Add("-"); // 標準入力
                continue;
            }
            
            if (isRecursive)
            {
                var expandedFiles = await ExpandRecursiveAsync(filePattern, options, cancellationToken);
                files.AddRange(expandedFiles);
            }
            else if (_fileSystem.FileExists(filePattern))
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

    private Task<IEnumerable<string>> ExpandRecursiveAsync(string path, IOptionContext options, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        
        if (_fileSystem.DirectoryExists(path))
        {
            var searchOption = SearchOption.AllDirectories;
            var allFiles = _fileSystem.EnumerateFiles(path, "*", searchOption);
            
            foreach (var file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldIncludeFile(file, options))
                {
                    files.Add(file);
                }
            }
        }
        else if (_fileSystem.FileExists(path))
        {
            files.Add(path);
        }
        
        return Task.FromResult<IEnumerable<string>>(files);
    }

    private IEnumerable<string> ExpandGlobPattern(string pattern)
    {
        try
        {
            var directory = _pathHelper.GetDirectoryName(pattern) ?? ".";
            var fileName = _pathHelper.GetFileName(pattern);
            
            if (_fileSystem.DirectoryExists(directory))
            {
                return _fileSystem.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly);
            }
        }
        catch (Exception)
        {
            // グロブ展開に失敗した場合はパターンをそのまま返す
        }
        
        return [pattern];
    }

    /// <summary>
    /// ファイルサイズに応じた最適なバッファサイズを計算
    /// </summary>
    /// <param name="fileSize">ファイルサイズ（バイト）</param>
    /// <returns>最適なバッファサイズ</returns>
    private static int GetOptimalBufferSize(long fileSize)
    {
        // 小さなファイル（1KB未満）: 1KB
        if (fileSize < 1024)
            return 1024;
        
        // 中程度のファイル（1MB未満）: 4KB
        if (fileSize < 1024 * 1024)
            return 4096;
        
        // 大きなファイル（10MB未満）: 8KB
        if (fileSize < 10 * 1024 * 1024)
            return 8192;
        
        // 非常に大きなファイル: 16KB
        return 16384;
    }

    private bool ShouldIncludeFile(string filePath, IOptionContext options)
    {
        var fileName = _pathHelper.GetFileName(filePath);
        
        // 除外パターンのチェック（StringComparison最適化）
        var excludePattern = options.GetStringValue(OptionNames.ExcludePattern);
        if (!string.IsNullOrEmpty(excludePattern))
        {
            // 単純な文字列比較であればRegexよりも高速
            if (excludePattern.Contains('*') || excludePattern.Contains('?'))
            {
                if (Regex.IsMatch(fileName, excludePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    return false;
            }
            else
            {
                // 完全一致比較の場合はStringComparison.OrdinalIgnoreCaseを使用
                if (fileName.Equals(excludePattern, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        
        // 包含パターンのチェック（StringComparison最適化）
        var includePattern = options.GetStringValue(OptionNames.IncludePattern);
        if (!string.IsNullOrEmpty(includePattern))
        {
            // 単純な文字列比較であればRegexよりも高速
            if (includePattern.Contains('*') || includePattern.Contains('?'))
            {
                if (!Regex.IsMatch(fileName, includePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    return false;
            }
            else
            {
                // 完全一致比較の場合はStringComparison.OrdinalIgnoreCaseを使用
                if (!fileName.Equals(includePattern, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        
        return true;
    }

    private async Task<FileResult> ProcessFileAsync(string filePath, IMatchStrategy strategy, IOptionContext options, CancellationToken cancellationToken)
    {
        // オプション値を一度だけ取得してキャッシュ
        var pattern = options.GetStringArgumentValue(ArgumentNames.Pattern) ?? "";
        var invertMatch = options.GetFlagValue(OptionNames.InvertMatch);
        var maxCount = options.GetIntValue(OptionNames.MaxCount);
        var contextBefore = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextBefore) ?? 0;
        var contextAfter = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextAfter) ?? 0;
        var needsContext = contextBefore > 0 || contextAfter > 0;
        
        // 標準入力の処理
        if (filePath == "-")
        {
            if (needsContext)
            {
                return await ProcessStandardInputWithContextAsync(strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken);
            }
            else
            {
                return await ProcessStandardInputAsync(strategy, options, pattern, invertMatch, maxCount, cancellationToken);
            }
        }
        
        // コンテキストが必要な場合は専用の処理を使用
        if (needsContext)
        {
            return await ProcessFileWithContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken);
        }
        
        // ArrayPoolを使用してメモリ効率を向上
        var estimatedSize = maxCount ?? 1000;
        var rentedArray = _matchPool.Rent(estimatedSize);
        var actualCount = 0;
        var hasMaxCountLimit = maxCount.HasValue;
        var maxCountValue = maxCount ?? int.MaxValue;
        
        try
        {
            var lineNumber = 0;
            
            // ファイルサイズに応じたバッファサイズの動的調整
            var fileInfo = _fileSystem.GetFileInfo(filePath);
            var bufferSize = GetOptimalBufferSize(fileInfo.Length);
            
            // ファイルの処理（大きなファイルでもメモリ効率的）
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            string? line;
            
            // 条件分岐の最適化: 反転マッチと通常マッチで処理パスを分離
            if (invertMatch)
            {
                // 反転マッチ専用の処理パス
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, filePath, lineNumber);
                    
                    // 反転マッチの場合は存在確認のみ行う
                    var hasMatches = !lineMatches.Any();
                    if (hasMatches)
                    {
                        // 反転マッチの場合は行全体をマッチとする（メモリ効率的）
                        var lineMemory = line.AsMemory();
                        rentedArray[actualCount++] = new MatchResult(filePath, lineNumber, line, lineMemory, 0, line.Length);
                        
                        // 最大マッチ数の制限チェック（最適化）
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            break;
                    }
                }
            }
            else
            {
                // 通常マッチ専用の処理パス
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, filePath, lineNumber);
                    
                    // 通常マッチの場合は実際のマッチを処理
                    foreach (var match in lineMatches)
                    {
                        rentedArray[actualCount++] = match;
                        
                        // 最大マッチ数の制限チェック（最適化）
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            // 最終的に必要な分だけコピーしてReadOnlyListを作成
            var results = new MatchResult[actualCount];
            Array.Copy(rentedArray, results, actualCount);
            
            return new FileResult(filePath, results.AsReadOnly(), actualCount);
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
        finally
        {
            _matchPool.Return(rentedArray, clearArray: true);
        }
    }

    private async Task<FileResult> ProcessFileWithContextAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            // ファイルサイズに応じたバッファサイズの動的調整
            var fileInfo = _fileSystem.GetFileInfo(filePath);
            var bufferSize = GetOptimalBufferSize(fileInfo.Length);
            
            // ファイルの全行を読み込み
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            var allLines = new List<string>();
            var lineNumber = 0;
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                allLines.Add(line);
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            // マッチを見つけてコンテキストを含む結果を作成
            var matches = new List<MatchResult>();
            var contextualMatches = new List<ContextualMatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            for (int i = 0; i < allLines.Count; i++)
            {
                var currentLine = allLines[i];
                var currentLineNumber = i + 1;
                
                var lineMatches = strategy.FindMatches(currentLine, pattern, options, filePath, currentLineNumber);
                
                if (invertMatch)
                {
                    // 反転マッチの場合は存在確認のみ行う
                    var hasMatches = !lineMatches.Any();
                    if (hasMatches)
                    {
                        // 反転マッチの場合は行全体をマッチとする
                        var lineMemory = currentLine.AsMemory();
                        var match = new MatchResult(filePath, currentLineNumber, currentLine, lineMemory, 0, currentLine.Length);
                        matches.Add(match);
                        
                        // コンテキストを作成
                        var contextualMatch = CreateContextualMatch(match, allLines, i, contextBefore, contextAfter);
                        contextualMatches.Add(contextualMatch);
                        
                        // 最大マッチ数の制限チェック
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    // 通常マッチの場合は実際のマッチを処理
                    foreach (var match in lineMatches)
                    {
                        matches.Add(match);
                        
                        // コンテキストを作成
                        var contextualMatch = CreateContextualMatch(match, allLines, i, contextBefore, contextAfter);
                        contextualMatches.Add(contextualMatch);
                        
                        // 最大マッチ数の制限チェック
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return new FileResult(filePath, matches.AsReadOnly(), matches.Count, false, null, contextualMatches.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
    }

    private ContextualMatchResult CreateContextualMatch(MatchResult match, List<string> allLines, int matchIndex, int contextBefore, int contextAfter)
    {
        var beforeContext = new List<ContextLine>();
        var afterContext = new List<ContextLine>();
        
        // Before context
        for (int j = Math.Max(0, matchIndex - contextBefore); j < matchIndex; j++)
        {
            beforeContext.Add(new ContextLine(match.FileName, j + 1, allLines[j], false));
        }
        
        // After context
        for (int j = matchIndex + 1; j <= Math.Min(allLines.Count - 1, matchIndex + contextAfter); j++)
        {
            afterContext.Add(new ContextLine(match.FileName, j + 1, allLines[j], false));
        }
        
        return new ContextualMatchResult(match, beforeContext.AsReadOnly(), afterContext.AsReadOnly());
    }

    private async Task<FileResult> ProcessStandardInputAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        // ArrayPoolを使用してメモリ効率を向上
        var estimatedSize = maxCount ?? 1000;
        var rentedArray = _matchPool.Rent(estimatedSize);
        var actualCount = 0;
        var lineNumber = 0;
        var hasMaxCountLimit = maxCount.HasValue;
        var maxCountValue = maxCount ?? int.MaxValue;
        const string fileName = "(standard input)";
        
        try
        {
            string? line;
            
            // ファイルシステムの抽象化を使用して標準入力を取得
            using var reader = _fileSystem.GetStandardInput();
            
            // 条件分岐の最適化: 反転マッチと通常マッチで処理パスを分離
            if (invertMatch)
            {
                // 反転マッチ専用の処理パス
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    // 反転マッチの場合は存在確認のみ行う
                    var hasMatches = !lineMatches.Any();
                    if (hasMatches)
                    {
                        // 反転マッチの場合は行全体をマッチとする（メモリ効率的）
                        var lineMemory = line.AsMemory();
                        rentedArray[actualCount++] = new MatchResult(fileName, lineNumber, line, lineMemory, 0, line.Length);
                        
                        // 最大マッチ数の制限チェック（最適化）
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            break;
                    }
                }
            }
            else
            {
                // 通常マッチ専用の処理パス
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    // 通常マッチの場合は実際のマッチを処理
                    foreach (var match in lineMatches)
                    {
                        rentedArray[actualCount++] = match;
                        
                        // 最大マッチ数の制限チェック（最適化）
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            // 最終的に必要な分だけコピーしてReadOnlyListを作成
            var results = new MatchResult[actualCount];
            Array.Copy(rentedArray, results, actualCount);
            
            return new FileResult(fileName, results.AsReadOnly(), actualCount);
        }
        catch (Exception ex)
        {
            return new FileResult(fileName, [], 0, true, ex.Message);
        }
        finally
        {
            _matchPool.Return(rentedArray, clearArray: true);
        }
    }

    private async Task<FileResult> ProcessStandardInputWithContextAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            const string fileName = "(standard input)";
            
            // 標準入力の全行を読み込み
            using var reader = _fileSystem.GetStandardInput();
            
            var allLines = new List<string>();
            var lineNumber = 0;
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                allLines.Add(line);
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            // マッチを見つけてコンテキストを含む結果を作成
            var matches = new List<MatchResult>();
            var contextualMatches = new List<ContextualMatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            for (int i = 0; i < allLines.Count; i++)
            {
                var currentLine = allLines[i];
                var currentLineNumber = i + 1;
                
                var lineMatches = strategy.FindMatches(currentLine, pattern, options, fileName, currentLineNumber);
                
                if (invertMatch)
                {
                    // 反転マッチの場合は存在確認のみ行う
                    var hasMatches = !lineMatches.Any();
                    if (hasMatches)
                    {
                        // 反転マッチの場合は行全体をマッチとする
                        var lineMemory = currentLine.AsMemory();
                        var match = new MatchResult(fileName, currentLineNumber, currentLine, lineMemory, 0, currentLine.Length);
                        matches.Add(match);
                        
                        // コンテキストを作成
                        var contextualMatch = CreateContextualMatch(match, allLines, i, contextBefore, contextAfter);
                        contextualMatches.Add(contextualMatch);
                        
                        // 最大マッチ数の制限チェック
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    // 通常マッチの場合は実際のマッチを処理
                    foreach (var match in lineMatches)
                    {
                        matches.Add(match);
                        
                        // コンテキストを作成
                        var contextualMatch = CreateContextualMatch(match, allLines, i, contextBefore, contextAfter);
                        contextualMatches.Add(contextualMatch);
                        
                        // 最大マッチ数の制限チェック
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return new FileResult(fileName, matches.AsReadOnly(), matches.Count, false, null, contextualMatches.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new FileResult("(standard input)", [], 0, true, ex.Message);
        }
    }
}
