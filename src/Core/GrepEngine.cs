using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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
                return await ProcessStandardInputWithOptimizedContextAsync(strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken);
            }
            else
            {
                return await ProcessStandardInputOptimizedAsync(strategy, options, pattern, invertMatch, maxCount, cancellationToken);
            }
        }
        
        // コンテキストが必要な場合は最適化された専用の処理を使用
        if (needsContext)
        {
            return await ProcessFileWithOptimizedContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken);
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

    // 古いメソッドは削除されました - ProcessFileWithOptimizedContextAsync を使用してください

    /// <summary>
    /// コンテキスト重複の最適化されたマッチ処理
    /// </summary>
    private async Task<FileResult> ProcessFileWithOptimizedContextAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = _fileSystem.GetFileInfo(filePath);
            var bufferSize = GetOptimalBufferSize(fileInfo.Length);
            
            // 大きなファイルの場合は部分読み込みを使用
            const long largeFileThreshold = 50 * 1024 * 1024; // 50MB
            if (fileInfo.Length > largeFileThreshold)
            {
                return await ProcessLargeFileWithStreamingContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken);
            }
            
            // 通常サイズのファイルは全行読み込み
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            // 初期容量を適切に設定
            var estimatedLines = Math.Max(100, (int)(fileInfo.Length / 50)); // 平均50バイト/行と仮定
            var allLines = new List<string>(estimatedLines);
            var lineNumber = 0;
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                allLines.Add(line);
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            // マッチ行のインデックスを事前に収集
            var matchingIndices = new List<int>();
            var matches = new List<MatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            // 第1パス: マッチ行の特定
            for (int i = 0; i < allLines.Count; i++)
            {
                var currentLine = allLines[i];
                var currentLineNumber = i + 1;
                
                var lineMatches = strategy.FindMatches(currentLine, pattern, options, filePath, currentLineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        matchingIndices.Add(i);
                        var lineMemory = currentLine.AsMemory();
                        matches.Add(new MatchResult(filePath, currentLineNumber, currentLine, lineMemory, 0, currentLine.Length));
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    if (lineMatches.Any())
                    {
                        matchingIndices.Add(i);
                        matches.AddRange(lineMatches);
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
            }
            
            // 第2パス: コンテキスト範囲の最適化
            var contextualMatches = CreateOptimizedContextualMatches(matches, allLines, matchingIndices, contextBefore, contextAfter);
            
            return new FileResult(filePath, matches.AsReadOnly(), matches.Count, false, null, contextualMatches.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
    }

    /// <summary>
    /// 大きなファイル用のストリーミング型コンテキスト処理
    /// </summary>
    private async Task<FileResult> ProcessLargeFileWithStreamingContextAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = _fileSystem.GetFileInfo(filePath);
            var bufferSize = GetOptimalBufferSize(fileInfo.Length);
            
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            var matches = new List<MatchResult>();
            var contextualMatches = new List<ContextualMatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            // 循環バッファを使用したコンテキスト管理
            var contextWindow = Math.Max(contextBefore, contextAfter);
            var lineBuffer = new Queue<(int LineNumber, string Content)>(contextWindow * 2 + 1);
            var matchBuffer = new Queue<(int LineNumber, MatchResult Match)>();
            
            var lineNumber = 0;
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                
                // バッファに現在の行を追加
                lineBuffer.Enqueue((lineNumber, line));
                
                // バッファサイズを制限
                if (lineBuffer.Count > contextWindow * 2 + 1)
                {
                    lineBuffer.Dequeue();
                }
                
                // マッチ判定
                var lineMatches = strategy.FindMatches(line, pattern, options, filePath, lineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        var lineMemory = line.AsMemory();
                        var match = new MatchResult(filePath, lineNumber, line, lineMemory, 0, line.Length);
                        matchBuffer.Enqueue((lineNumber, match));
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
                            matchBuffer.Enqueue((lineNumber, match));
                            matches.Add(match);
                            
                            if (hasMaxCountLimit && matches.Count >= maxCountValue)
                                goto exitLoop;
                        }
                    }
                }
                
                // コンテキストが十分にバッファされた場合に処理
                if (lineBuffer.Count >= contextBefore + 1)
                {
                    ProcessBufferedMatches(matchBuffer, lineBuffer, contextBefore, contextAfter, contextualMatches);
                }
            }
            
            exitLoop:
            
            // 残りのマッチを処理
            ProcessBufferedMatches(matchBuffer, lineBuffer, contextBefore, contextAfter, contextualMatches);
            
            return new FileResult(filePath, matches.AsReadOnly(), matches.Count, false, null, contextualMatches.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, [], 0, true, ex.Message);
        }
    }

    /// <summary>
    /// バッファされたマッチを処理してコンテキストを作成
    /// </summary>
    private static void ProcessBufferedMatches(Queue<(int LineNumber, MatchResult Match)> matchBuffer, Queue<(int LineNumber, string Content)> lineBuffer, int contextBefore, int contextAfter, List<ContextualMatchResult> contextualMatches)
    {
        var bufferArray = lineBuffer.ToArray();
        
        while (matchBuffer.Count > 0)
        {
            var (matchLineNumber, match) = matchBuffer.Dequeue();
            
            // 現在のマッチがコンテキストを作成できる位置にあるかチェック
            if (bufferArray.Length > contextBefore)
            {
                var contextualMatch = CreateContextualMatchFromBuffer(match, bufferArray, contextBefore, contextAfter);
                contextualMatches.Add(contextualMatch);
            }
        }
    }

    /// <summary>
    /// バッファからコンテキストマッチを作成
    /// </summary>
    private static ContextualMatchResult CreateContextualMatchFromBuffer(MatchResult match, (int LineNumber, string Content)[] buffer, int contextBefore, int contextAfter)
    {
        var beforeContext = new List<ContextLine>();
        var afterContext = new List<ContextLine>();
        
        var matchLineNumber = match.LineNumber;
        
        foreach (var (lineNumber, content) in buffer)
        {
            if (lineNumber < matchLineNumber && lineNumber >= matchLineNumber - contextBefore)
            {
                beforeContext.Add(new ContextLine(match.FileName, lineNumber, content, false));
            }
            else if (lineNumber > matchLineNumber && lineNumber <= matchLineNumber + contextAfter)
            {
                afterContext.Add(new ContextLine(match.FileName, lineNumber, content, false));
            }
        }
        
        return new ContextualMatchResult(match, beforeContext.AsReadOnly(), afterContext.AsReadOnly());
    }

    /// <summary>
    /// 最適化されたコンテキストマッチの作成
    /// </summary>
    private static ReadOnlyCollection<ContextualMatchResult> CreateOptimizedContextualMatches(List<MatchResult> matches, List<string> allLines, List<int> matchingIndices, int contextBefore, int contextAfter)
    {
        var contextualMatches = new List<ContextualMatchResult>();
        var processedRanges = new HashSet<(int Start, int End)>();
        
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var matchIndex = matchingIndices[Math.Min(i, matchingIndices.Count - 1)];
            
            var startLine = Math.Max(0, matchIndex - contextBefore);
            var endLine = Math.Min(allLines.Count - 1, matchIndex + contextAfter);
            var range = (startLine, endLine);
            
            // 重複する範囲をスキップ
            if (processedRanges.Contains(range))
                continue;
            
            processedRanges.Add(range);
            
            var beforeContext = new List<ContextLine>();
            var afterContext = new List<ContextLine>();
            
            // Before context
            for (int j = startLine; j < matchIndex; j++)
            {
                beforeContext.Add(new ContextLine(match.FileName, j + 1, allLines[j], false));
            }
            
            // After context
            for (int j = matchIndex + 1; j <= endLine; j++)
            {
                afterContext.Add(new ContextLine(match.FileName, j + 1, allLines[j], false));
            }
            
            contextualMatches.Add(new ContextualMatchResult(match, beforeContext.AsReadOnly(), afterContext.AsReadOnly()));
        }
        
        return contextualMatches.AsReadOnly();
    }

    /// <summary>
    /// 最適化された標準入力処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputOptimizedAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
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
            using var reader = _fileSystem.GetStandardInput();
            
            if (invertMatch)
            {
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    if (!lineMatches.Any())
                    {
                        var lineMemory = line.AsMemory();
                        rentedArray[actualCount++] = new MatchResult(fileName, lineNumber, line, lineMemory, 0, line.Length);
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            break;
                    }
                }
            }
            else
            {
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var lineMatches = strategy.FindMatches(line, pattern, options, fileName, lineNumber);
                    
                    foreach (var match in lineMatches)
                    {
                        rentedArray[actualCount++] = match;
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
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

    /// <summary>
    /// 最適化された標準入力のコンテキスト処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputWithOptimizedContextAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, int contextBefore, int contextAfter, CancellationToken cancellationToken)
    {
        try
        {
            const string fileName = "(standard input)";
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
            
            var matchingIndices = new List<int>();
            var matches = new List<MatchResult>();
            var hasMaxCountLimit = maxCount.HasValue;
            var maxCountValue = maxCount ?? int.MaxValue;
            
            for (int i = 0; i < allLines.Count; i++)
            {
                var currentLine = allLines[i];
                var currentLineNumber = i + 1;
                
                var lineMatches = strategy.FindMatches(currentLine, pattern, options, fileName, currentLineNumber);
                
                if (invertMatch)
                {
                    if (!lineMatches.Any())
                    {
                        matchingIndices.Add(i);
                        var lineMemory = currentLine.AsMemory();
                        matches.Add(new MatchResult(fileName, currentLineNumber, currentLine, lineMemory, 0, currentLine.Length));
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    if (lineMatches.Any())
                    {
                        matchingIndices.Add(i);
                        matches.AddRange(lineMatches);
                        
                        if (hasMaxCountLimit && matches.Count >= maxCountValue)
                            break;
                    }
                }
            }
            
            var contextualMatches = CreateOptimizedContextualMatches(matches, allLines, matchingIndices, contextBefore, contextAfter);
            
            return new FileResult(fileName, matches.AsReadOnly(), matches.Count, false, null, contextualMatches);
        }
        catch (Exception ex)
        {
            return new FileResult("(standard input)", [], 0, true, ex.Message);
        }
    }
}
