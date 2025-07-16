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

    /// <summary>
    /// globパターンを正規表現パターンに変換する
    /// </summary>
    /// <param name="globPattern">globパターン（例: *.cs, test?.txt）</param>
    /// <returns>正規表現パターン</returns>
    private static string ConvertGlobToRegex(string globPattern)
    {
        if (string.IsNullOrEmpty(globPattern))
            return string.Empty;
        
        // Escape the entire glob pattern
        string escapedPattern = Regex.Escape(globPattern);
        
        // Replace escaped glob tokens with regex equivalents
        escapedPattern = escapedPattern.Replace(@"\*", ".*").Replace(@"\?", ".");
        
        // Add start and end anchors
        return $"^{escapedPattern}$";
    }

    private bool ShouldIncludeFile(string filePath, IOptionContext options)
    {
        var fileName = _pathHelper.GetFileName(filePath);
        
        // 除外パターンのチェック（globパターン対応）
        var excludePattern = options.GetStringValue(OptionNames.ExcludePattern);
        if (!string.IsNullOrEmpty(excludePattern))
        {
            // globパターンかどうかをチェック
            if (excludePattern.Contains('*') || excludePattern.Contains('?'))
            {
                var regexPattern = ConvertGlobToRegex(excludePattern);
                if (Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    return false;
            }
            else
            {
                // 完全一致比較の場合はStringComparison.OrdinalIgnoreCaseを使用
                if (fileName.Equals(excludePattern, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        
        // 包含パターンのチェック（globパターン対応）
        var includePattern = options.GetStringValue(OptionNames.IncludePattern);
        if (!string.IsNullOrEmpty(includePattern))
        {
            // globパターンかどうかをチェック
            if (includePattern.Contains('*') || includePattern.Contains('?'))
            {
                var regexPattern = ConvertGlobToRegex(includePattern);
                if (!Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
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
        
        // 標準入力の処理
        if (filePath == "-")
        {
            return await ProcessStandardInputAsync(strategy, options, pattern, invertMatch, maxCount, cancellationToken);
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
}
