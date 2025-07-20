using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

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
public class ParallelGrepEngine(
    IMatchStrategyFactory strategyFactory,
    IFileSystem fileSystem,
    IPath pathHelper,
    IFileSearchService fileSearchService,
    IPerformanceOptimizer performanceOptimizer,
    IMatchResultPool matchResultPool) : IGrepEngine
{
    private readonly IMatchStrategyFactory _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IPath _pathHelper = pathHelper ?? throw new ArgumentNullException(nameof(pathHelper));
    private readonly IFileSearchService _fileSearchService = fileSearchService ?? throw new ArgumentNullException(nameof(fileSearchService));
    private readonly IPerformanceOptimizer _performanceOptimizer = performanceOptimizer ?? throw new ArgumentNullException(nameof(performanceOptimizer));
    private readonly IMatchResultPool _matchResultPool = matchResultPool ?? throw new ArgumentNullException(nameof(matchResultPool));
public async Task<SearchResult> SearchAsync(IOptionContext options, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var strategy = _strategyFactory.CreateStrategy(options);
        
        try
        {
            var files = await _fileSearchService.ExpandFilesAsync(options, cancellationToken).ConfigureAwait(false);
            var filesList = files.ToList();
            var results = new ConcurrentBag<FileResult>();
            
            // ファイル数に基づいて並列度を動的に調整
            var optimalParallelism = _performanceOptimizer.CalculateOptimalParallelism(filesList.Count);
            
            // 0を防止する安全策
            if (optimalParallelism <= 0)
                optimalParallelism = 1;
            
            // 並列処理でファイルを処理
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = optimalParallelism
            };
            
            await Parallel.ForEachAsync(filesList, parallelOptions, async (file, ct) =>
            {
                var result = await ProcessFileAsync(file, strategy, options, ct).ConfigureAwait(false);
                results.Add(result);
            }).ConfigureAwait(false);
            
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
                return await ProcessStandardInputWithContextAsync(strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return await ProcessStandardInputAsync(strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
            }
        }
        
        // コンテキストが必要な場合は専用の処理を使用
        if (needsContext)
        {
            return await ProcessFileWithContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
        }
        
        // 小さなファイルの場合は高速パスを使用
        var fileInfo = _fileSystem.GetFileInfo(filePath);
        if (fileInfo?.Length <= 4096) // 4KB以下
        {
            return await ProcessSmallFileAsync(filePath, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
        }
        
        // 通常の処理（IAsyncEnumerableを使用したストリーミング）
        return await ProcessFileStreamingAsync(filePath, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ファイルのコンテキスト付きマッチ処理
    /// </summary>
    private async Task<FileResult> ProcessFileWithContextAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        var fileInfo = _fileSystem.GetFileInfo(filePath);
        var bufferSize = _performanceOptimizer.GetOptimalBufferSize(fileInfo.Length);
        
        using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var reader = new StreamReader(fileStream, Encoding.UTF8);
        
        // TextReaderをIAsyncEnumerable<string>に変換
        var lineSource = ReadLinesFromReaderAsync(reader, cancellationToken);
        return await ProcessCoreWithContextAsync(filePath, lineSource, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// TextReaderからIAsyncEnumerable<string>を作成
    /// </summary>
    private static async IAsyncEnumerable<string> ReadLinesFromReaderAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// 共通の処理コア（TextReader使用）
    /// </summary>
    private async Task<FileResult> ProcessCoreAsync(string fileName, TextReader reader, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var estimatedSize = maxCount ?? 1000;
        using var pooledArray = _matchResultPool.Rent(estimatedSize);
        var lineNumber = 0;
        var hasMaxCountLimit = maxCount.HasValue;
        var maxCountValue = maxCount ?? int.MaxValue;
        
        try
        {
            string? line;
            
            if (invertMatch)
            {
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    if (!lineMatches.Any())
                    {
                        var lineMemory = line.AsMemory();
                        var match = new MatchResult(fileName, lineNumber, line, lineMemory, 0, line.Length);
                        _matchResultPool.AddMatch(pooledArray, match, maxCount);
                        
                        if (hasMaxCountLimit && pooledArray.Count >= maxCountValue)
                            break;
                    }
                }
            }
            else
            {
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    foreach (var match in lineMatches)
                    {
                        _matchResultPool.AddMatch(pooledArray, match, maxCount);
                        
                        if (hasMaxCountLimit && pooledArray.Count >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return _matchResultPool.CreateFileResult(fileName, pooledArray);
        }
        catch (Exception ex)
        {
            return new FileResult(fileName, [], 0, true, ex.Message);
        }
    }

    /// <summary>
    /// 標準入力処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        const string fileName = "(standard input)";
        using var reader = _fileSystem.GetStandardInput();
        return await ProcessCoreAsync(fileName, reader, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 標準入力のコンテキスト処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputWithContextAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        const string fileName = "(standard input)";
        using var reader = _fileSystem.GetStandardInput();
        
        // TextReaderをIAsyncEnumerable<string>に変換
        var lineSource = ReadLinesFromReaderAsync(reader, cancellationToken);
        return await ProcessCoreWithContextAsync(fileName, lineSource, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 共通のコンテキスト付き処理コア（真のストリーミング型）
    /// </summary>
    private static async Task<FileResult> ProcessCoreWithContextAsync(string fileName, IAsyncEnumerable<string> lineSource, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            var matches = new List<MatchResult>();
            var contextualMatches = new List<ContextualMatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            // 効率的な循環バッファを使用したコンテキスト管理
            var contextWindow = Math.Max(contextBefore, contextAfter);
            var lineBuffer = new Queue<(int LineNumber, string Content)>(contextWindow * 2 + 1);
            var pendingMatches = new List<(int LineNumber, MatchResult Match)>();
            
            var lineNumber = 0;
            
            await foreach (var line in lineSource.ConfigureAwait(false))
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                
                // バッファに現在の行を追加
                lineBuffer.Enqueue((lineNumber, line));
                
                // バッファサイズを制限（古い行を削除）
                while (lineBuffer.Count > contextWindow * 2 + 1)
                {
                    lineBuffer.Dequeue();
                }
                
                // マッチ判定
                var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        var lineMemory = line.AsMemory();
                        var match = new MatchResult(fileName, lineNumber, line, lineMemory, 0, line.Length);
                        pendingMatches.Add((lineNumber, match));
                        matches.Add(match);
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    if (lineMatches.Any())
                    {
                        foreach (var match in lineMatches)
                        {
                            pendingMatches.Add((lineNumber, match));
                            matches.Add(match);
                            
                            if (hasMaxCountLimit && matches.Count >= maxCountValue)
                                goto exitLoop;
                        }
                    }
                }
                
                // 十分なコンテキストが蓄積された場合に処理
                if (lineBuffer.Count >= contextBefore + contextAfter + 1)
                {
                    ProcessReadyMatches(pendingMatches, lineBuffer, contextBefore, contextAfter, contextualMatches);
                }
            }
            
            exitLoop:
            
            // 残りの保留中マッチを処理
            ProcessReadyMatches(pendingMatches, lineBuffer, contextBefore, contextAfter, contextualMatches, true);
            
            return new FileResult(fileName, matches.AsReadOnly(), matches.Count, false, null, contextualMatches.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new FileResult(fileName, [], 0, true, ex.Message);
        }
    }

    /// <summary>
    /// 準備ができたマッチを処理してコンテキスト付き結果を作成
    /// </summary>
    private static void ProcessReadyMatches(List<(int LineNumber, MatchResult Match)> pendingMatches, Queue<(int LineNumber, string Content)> lineBuffer, int contextBefore, int contextAfter, List<ContextualMatchResult> contextualMatches, bool processAll = false)
    {
        var bufferArray = lineBuffer.ToArray();
        
        for (int i = pendingMatches.Count - 1; i >= 0; i--)
        {
            var (matchLineNumber, match) = pendingMatches[i];
            
            // バッファ内でマッチ行の位置を特定
            int matchPosition = -1;
            for (int j = 0; j < bufferArray.Length; j++)
            {
                if (bufferArray[j].LineNumber == matchLineNumber)
                {
                    matchPosition = j;
                    break;
                }
            }
            
            if (matchPosition == -1)
                continue; // マッチ行がバッファにない（古すぎる）
            
            // コンテキストを作成するのに十分な行があるかチェック
            var hasEnoughBefore = matchPosition >= contextBefore;
            var hasEnoughAfter = (bufferArray.Length - matchPosition - 1) >= contextAfter;
            
            if (processAll || (hasEnoughBefore && hasEnoughAfter))
            {
                var contextualMatch = CreateContextualMatchFromBuffer(match, bufferArray, matchPosition, contextBefore, contextAfter);
                contextualMatches.Add(contextualMatch);
                pendingMatches.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// バッファからコンテキスト付きマッチを作成（改良版）
    /// </summary>
    private static ContextualMatchResult CreateContextualMatchFromBuffer(MatchResult match, (int LineNumber, string Content)[] buffer, int matchBufferPosition, int contextBefore, int contextAfter)
    {
        var beforeContext = new List<ContextLine>();
        var afterContext = new List<ContextLine>();
        
        // Before context
        var beforeStart = Math.Max(0, matchBufferPosition - contextBefore);
        for (int i = beforeStart; i < matchBufferPosition; i++)
        {
            var (lineNumber, content) = buffer[i];
            beforeContext.Add(new ContextLine(match.FileName, lineNumber, content, false));
        }
        
        // After context  
        var afterEnd = Math.Min(buffer.Length, matchBufferPosition + contextAfter + 1);
        for (int i = matchBufferPosition + 1; i < afterEnd; i++)
        {
            var (lineNumber, content) = buffer[i];
            afterContext.Add(new ContextLine(match.FileName, lineNumber, content, false));
        }
        
        return new ContextualMatchResult(match, beforeContext.AsReadOnly(), afterContext.AsReadOnly());
    }

    /// <summary>
    /// 共通のストリーミング処理コア
    /// </summary>
    private async Task<FileResult> ProcessCoreStreamingAsync(string fileName, IAsyncEnumerable<ReadOnlyMemory<char>> lineSource, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var estimatedSize = maxCount ?? 1000;
        using var pooledArray = _matchResultPool.Rent(estimatedSize);
        var lineNumber = 0;
        var hasMaxCountLimit = maxCount.HasValue;
        var maxCountValue = maxCount ?? int.MaxValue;
        
        try
        {
            await foreach (var lineMemory in lineSource.ConfigureAwait(false))
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                
                var line = lineMemory.ToString();
                var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        var match = new MatchResult(fileName, lineNumber, line, lineMemory, 0, lineMemory.Length);
                        _matchResultPool.AddMatch(pooledArray, match, maxCount);
                        
                        if (hasMaxCountLimit && pooledArray.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    foreach (var match in lineMatches)
                    {
                        _matchResultPool.AddMatch(pooledArray, match, maxCount);
                        
                        if (hasMaxCountLimit && pooledArray.Count >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return _matchResultPool.CreateFileResult(fileName, pooledArray);
        }
        catch (Exception ex)
        {
            return new FileResult(fileName, [], 0, true, ex.Message);
        }
    }

    /// <summary>
    /// IAsyncEnumerableを使用したストリーミング処理（ReadOnlyMemoryによるゼロコピー最適化）
    /// </summary>
    private async Task<FileResult> ProcessFileStreamingAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var lineSource = _fileSystem.ReadLinesAsMemoryAsync(filePath, cancellationToken);
        return await ProcessCoreStreamingAsync(filePath, lineSource, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 小さなファイル用の高速処理パス（ValueTaskを使用）
    /// </summary>
    private async ValueTask<FileResult> ProcessSmallFileAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        const int smallFileThreshold = 1024 * 4; // 4KB
        
        var fileInfo = _fileSystem.GetFileInfo(filePath);
        if (fileInfo.Length > smallFileThreshold)
        {
            // 通常の処理にフォールバック
            return await ProcessFileStreamingAsync(filePath, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
        }
        
        try
        {
            // 小さなファイルは一度に全て読み込み
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, bufferSize: 1024);
            
            var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var lines = content.Split('\n', StringSplitOptions.None);
            
            var matches = new List<MatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;
                
                if (line.EndsWith('\r'))
                    line = line[..^1]; // Remove trailing carriage return
                
                var lineMatches = strategy.FindMatches(line, pattern, options, filePath, lineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        var lineMemory = line.AsMemory();
                        matches.Add(new MatchResult(filePath, lineNumber, line, lineMemory, 0, line.Length));
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    matches.AddRange(lineMatches);
                    
                    if (hasMaxCountLimit && matches.Count >= maxCountValue)
                        break;
                }
            }
            
            return new FileResult(filePath, matches.AsReadOnly(), matches.Count);
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
    }
}
