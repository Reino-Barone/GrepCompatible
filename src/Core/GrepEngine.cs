using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Text;

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
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private static readonly object _regexCacheLock = new();
    
    // 正規表現の特殊文字セット（.NET 8+のSearchValues使用）
    private static readonly SearchValues<char> _regexSpecialChars = SearchValues.Create([
        '.', '^', '$', '(', ')', '[', ']', '{', '}', '|', '\\', '+', '*', '?'
    ]);

    public async Task<SearchResult> SearchAsync(IOptionContext options, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var strategy = _strategyFactory.CreateStrategy(options);
        
        try
        {
            var files = await ExpandFilesAsync(options, cancellationToken).ConfigureAwait(false);
            var filesList = files.ToList();
            var results = new ConcurrentBag<FileResult>();
            
            // ファイル数に基づいて並列度を動的に調整
            var optimalParallelism = CalculateOptimalParallelism(filesList.Count);
            
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

    /// <summary>
    /// ファイル数に基づいて最適な並列度を計算
    /// </summary>
    /// <param name="fileCount">処理するファイル数</param>
    /// <returns>最適な並列度</returns>
    private static int CalculateOptimalParallelism(int fileCount)
    {
        var processorCount = Environment.ProcessorCount;
        
        return fileCount switch
        {
            // MaxDegreeOfParallelismは0にできないため、最小値は1に設定
            <= 0 => 1,
            
            // 小さなファイル数の場合は並列度を制限
            <= 4 => Math.Min(fileCount, processorCount),
            
            // 中程度のファイル数の場合はCPUコア数を使用
            <= 20 => processorCount,
            
            // 大量のファイルの場合は少し並列度を上げる
            _ => Math.Min(processorCount * 2, fileCount)
        };
    }

    /// <summary>
    /// ArrayPoolを使用した動的配列管理
    /// </summary>
    private static (MatchResult[] array, int newSize) ResizeArrayIfNeeded(MatchResult[] currentArray, int currentCount, int currentSize, int maxCount)
    {
        // 最大数制限がある場合は現在の配列を使用
        if (maxCount > 0 && currentCount >= maxCount)
            return (currentArray, currentSize);
        
        // 配列がフルになった場合は拡張
        if (currentCount >= currentSize)
        {
            var newSize = Math.Min(currentSize * 2, maxCount > 0 ? maxCount : currentSize * 2);
            var newArray = _matchPool.Rent(newSize);
            Array.Copy(currentArray, newArray, currentCount);
            _matchPool.Return(currentArray, clearArray: true);
            return (newArray, newSize);
        }
        
        return (currentArray, currentSize);
    }

    /// <summary>
    /// ArrayPoolを使用した共通の配列処理ロジック
    /// </summary>
    private static void AddMatchToArray(ref MatchResult[] array, ref int arraySize, ref int actualCount, MatchResult match, int? maxCount)
    {
        // 配列のリサイズが必要かチェック
        (array, arraySize) = ResizeArrayIfNeeded(array, actualCount, arraySize, maxCount ?? 0);
        
        // マッチを配列に追加
        array[actualCount++] = match;
    }

    /// <summary>
    /// 共通の結果作成ロジック
    /// </summary>
    private static FileResult CreateFileResultFromArray(string fileName, MatchResult[] rentedArray, int actualCount)
    {
        var results = new MatchResult[actualCount];
        Array.Copy(rentedArray, results, actualCount);
        return new FileResult(fileName, results.AsReadOnly(), actualCount);
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
                var expandedFiles = await ExpandRecursiveAsync(filePattern, options, cancellationToken).ConfigureAwait(false);
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

    private async Task<IEnumerable<string>> ExpandRecursiveAsync(string path, IOptionContext options, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        
        if (_fileSystem.DirectoryExists(path))
        {
            var searchOption = SearchOption.AllDirectories;
            
            // 非同期ファイル列挙を使用してメモリ効率を向上
            await foreach (var file in _fileSystem.EnumerateFilesAsync(path, "*", searchOption, cancellationToken).ConfigureAwait(false))
            {
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
        
        return files;
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
        
        // Spanを使用してメモリ効率的に処理
        ReadOnlySpan<char> pattern = globPattern.AsSpan();
        
        // 結果を格納するためのSpanバッファ（推定サイズ）
        Span<char> buffer = stackalloc char[globPattern.Length * 2];
        var resultLength = 0;
        
        // 開始アンカーを追加
        buffer[resultLength++] = '^';

        for (int i = 0, len = pattern.Length; i < len; i++)
        {
            char c = pattern[i];
            
            switch (c)
            {
                case '*':
                    // * → .*
                    buffer[resultLength++] = '.';
                    buffer[resultLength++] = '*';
                    break;
                
                case '?':
                    // ? → .
                    buffer[resultLength++] = '.';
                    break;
                
                case '.':
                case '^':
                case '$':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '|':
                case '\\':
                case '+':
                    // 正規表現の特殊文字をエスケープ
                    buffer[resultLength++] = '\\';
                    buffer[resultLength++] = c;
                    break;
                
                default:
                    // 通常の文字はそのまま
                    buffer[resultLength++] = c;
                    break;
            }
            
            // バッファオーバーフロー防止
            if (resultLength >= buffer.Length - 2)
            {
                // バッファが足りない場合は従来の方法にフォールバック
                var sb = new StringBuilder();
                sb.Append('^');
                foreach (char _c in globPattern)
                {
                    switch (_c)
                    {
                        case '*':
                            sb.Append(".*");
                            break;
                        case '?':
                            sb.Append('.');
                            break;
                        case '.':
                        case '^':
                        case '$':
                        case '(':
                        case ')':
                        case '[':
                        case ']':
                        case '{':
                        case '}':
                        case '|':
                        case '\\':
                        case '+':
                            sb.Append('\\').Append(_c);
                            break;
                        default:
                            sb.Append(_c);
                            break;
                    }
                }
                sb.Append('$');
                return sb.ToString();
            }
        }
        
        // 終了アンカーを追加
        buffer[resultLength++] = '$';
        
        // Spanから文字列を作成
        return new string(buffer[..resultLength]);
    }

    /// <summary>
    /// SearchValues&lt;char&gt;を使用したより効率的なglobパターン変換
    /// </summary>
    /// <param name="globPattern">globパターン</param>
    /// <returns>正規表現パターン</returns>
    private static string ConvertGlobToRegexOptimized(string globPattern)
    {
        if (string.IsNullOrEmpty(globPattern))
            return string.Empty;
        
        ReadOnlySpan<char> pattern = globPattern.AsSpan();
        
        // 結果を格納するためのバッファ（推定サイズ）
        var buffer = new char[globPattern.Length * 2 + 2];
        var resultLength = 0;
        
        // 開始アンカーを追加
        buffer[resultLength++] = '^';
        
        int i = 0;
        while (i < pattern.Length)
        {
            // SearchValuesを使用して特殊文字の次の出現位置を効率的に検索
            int nextSpecialIndex = pattern[i..].IndexOfAny(_regexSpecialChars);
            
            if (nextSpecialIndex == -1)
            {
                // 特殊文字が見つからない場合、残りの文字をそのままコピー
                var remaining = pattern[i..];
                remaining.CopyTo(buffer.AsSpan(resultLength));
                resultLength += remaining.Length;
                break;
            }
            
            // 特殊文字までの通常文字をコピー
            var normalChars = pattern[i..(i + nextSpecialIndex)];
            normalChars.CopyTo(buffer.AsSpan(resultLength));
            resultLength += normalChars.Length;
            
            // 特殊文字を処理
            char specialChar = pattern[i + nextSpecialIndex];
            switch (specialChar)
            {
                case '*':
                    buffer[resultLength++] = '.';
                    buffer[resultLength++] = '*';
                    break;
                
                case '?':
                    buffer[resultLength++] = '.';
                    break;
                
                default:
                    // その他の特殊文字はエスケープ
                    buffer[resultLength++] = '\\';
                    buffer[resultLength++] = specialChar;
                    break;
            }
            
            i += nextSpecialIndex + 1;
        }
        
        // 終了アンカーを追加
        buffer[resultLength++] = '$';
        
        return new string(buffer, 0, resultLength);
    }

    /// <summary>
    /// パターンをコンパイルされた正規表現として取得（キャッシュ機能付き）
    /// </summary>
    /// <param name="pattern">パターン（globまたは正規表現）</param>
    /// <param name="isGlobPattern">globパターンかどうか</param>
    /// <returns>コンパイルされた正規表現</returns>
    private static Regex GetCompiledRegex(string pattern, bool isGlobPattern)
    {
        var key = isGlobPattern ? $"glob:{pattern}" : $"regex:{pattern}";
        
        return _regexCache.GetOrAdd(key, _ =>
        {
            var regexPattern = isGlobPattern ? ConvertGlobToRegexOptimized(pattern) : pattern;
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
    }

    /// <summary>
    /// 複数パターンのマッチングを実行
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="fullPath">フルパス（正規化済み）</param>
    /// <param name="patterns">パターンのリスト</param>
    /// <param name="isExclude">除外パターンかどうか</param>
    /// <returns>マッチしたかどうか</returns>
    private static bool MatchesAnyPattern(string fileName, string fullPath, IEnumerable<string> patterns, bool isExclude)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;

            var isGlobPattern = pattern.Contains('*') || pattern.Contains('?');
            
            bool matches;
            if (isGlobPattern)
            {
                var regex = GetCompiledRegex(pattern, true);
                
                // パターンに'/'が含まれる場合はフルパスでマッチング、そうでなければファイル名のみ
                var targetText = pattern.Contains('/') ? fullPath : fileName;
                matches = regex.IsMatch(targetText);
            }
            else
            {
                // 非globパターンの場合も同様にパスの判定を行う
                var targetText = pattern.Contains('/') ? fullPath : fileName;
                matches = targetText.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
                return true;
        }
        
        return false;
    }

    private bool ShouldIncludeFile(string filePath, IOptionContext options)
    {
        var fileName = _pathHelper.GetFileName(filePath);
        var normalizedPath = filePath.Replace('\\', '/'); // Windowsパスを正規化
        
        // 除外パターンのチェック（複数パターン対応）
        var excludePatterns = GetPatterns(options, OptionNames.ExcludePattern);
        if (excludePatterns.Count > 0)
        {
            if (MatchesAnyPattern(fileName, normalizedPath, excludePatterns, true))
                return false;
        }
        
        // 包含パターンのチェック（複数パターン対応）
        var includePatterns = GetPatterns(options, OptionNames.IncludePattern);
        if (includePatterns.Count > 0)
        {
            if (!MatchesAnyPattern(fileName, normalizedPath, includePatterns, false))
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// オプションから複数パターンを取得
    /// </summary>
    /// <param name="options">オプションコンテキスト</param>
    /// <param name="optionName">オプション名</param>
    /// <returns>パターンのリスト</returns>
    private static List<string> GetPatterns(IOptionContext options, OptionNames optionName)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // 後方互換性のため、まず単一の値を取得
        var singleValue = options.GetStringValue(optionName);
        if (!string.IsNullOrEmpty(singleValue))
        {
            // 単一値内でのコンマ・セミコロン区切りもサポート
            var splitPatterns = singleValue.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pattern in splitPatterns)
            {
                var trimmedPattern = pattern.Trim();
                if (!string.IsNullOrEmpty(trimmedPattern))
                {
                    patterns.Add(trimmedPattern);
                }
            }
        }
        
        // 複数の同名オプションの値を全て取得
        var allOptionValues = options.GetAllStringValues(optionName);
        if (allOptionValues != null)
        {
            foreach (var optionValue in allOptionValues)
            {
                if (string.IsNullOrEmpty(optionValue))
                    continue;
                
                // 各オプション値内でのコンマ・セミコロン区切りもサポート
                var splitPatterns = optionValue.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pattern in splitPatterns)
                {
                    var trimmedPattern = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmedPattern))
                    {
                        patterns.Add(trimmedPattern);
                    }
                }
            }
        }
        
        return [.. patterns];
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
                return await ProcessStandardInputWithOptimizedContextAsync(strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return await ProcessStandardInputOptimizedAsync(strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
            }
        }
        
        // コンテキストが必要な場合は最適化された専用の処理を使用
        if (needsContext)
        {
            return await ProcessFileWithOptimizedContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
        }
        
        // 小さなファイルの場合は高速パスを使用
        var fileInfo = _fileSystem.GetFileInfo(filePath);
        if (fileInfo.Length <= 4096) // 4KB以下
        {
            return await ProcessSmallFileAsync(filePath, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
        }
        
        // 通常の処理（IAsyncEnumerableを使用したストリーミング）
        return await ProcessFileStreamingAsync(filePath, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
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
                return await ProcessLargeFileWithStreamingContextAsync(filePath, strategy, options, pattern, invertMatch, maxCount, contextBefore, contextAfter, cancellationToken).ConfigureAwait(false);
            }
            
            // 通常サイズのファイルは全行読み込み
            using var fileStream = _fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            // 初期容量を適切に設定
            var estimatedLines = Math.Max(100, (int)(fileInfo.Length / 50)); // 平均50バイト/行と仮定
            var allLines = new List<string>(estimatedLines);
            var lineNumber = 0;
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
            
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
    /// 共通の最適化された処理コア（TextReader使用）
    /// </summary>
    private async Task<FileResult> ProcessCoreOptimizedAsync(string fileName, TextReader reader, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var estimatedSize = maxCount ?? 1000;
        var rentedArray = _matchPool.Rent(estimatedSize);
        var arraySize = estimatedSize;
        var actualCount = 0;
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
                        AddMatchToArray(ref rentedArray, ref arraySize, ref actualCount, match, maxCount);
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
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
                        AddMatchToArray(ref rentedArray, ref arraySize, ref actualCount, match, maxCount);
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return CreateFileResultFromArray(fileName, rentedArray, actualCount);
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
    /// 最適化された標準入力処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputOptimizedAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        const string fileName = "(standard input)";
        using var reader = _fileSystem.GetStandardInput();
        return await ProcessCoreOptimizedAsync(fileName, reader, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
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
            
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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

    /// <summary>
    /// 共通のストリーミング処理コア
    /// </summary>
    private async Task<FileResult> ProcessCoreStreamingAsync(string fileName, IAsyncEnumerable<ReadOnlyMemory<char>> lineSource, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var estimatedSize = maxCount ?? 1000;
        var rentedArray = _matchPool.Rent(estimatedSize);
        var arraySize = estimatedSize;
        var actualCount = 0;
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
                        AddMatchToArray(ref rentedArray, ref arraySize, ref actualCount, match, maxCount);
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            break;
                    }
                }
                else
                {
                    foreach (var match in lineMatches)
                    {
                        AddMatchToArray(ref rentedArray, ref arraySize, ref actualCount, match, maxCount);
                        
                        if (hasMaxCountLimit && actualCount >= maxCountValue)
                            goto exitLoop;
                    }
                }
            }
            
            exitLoop:
            
            return CreateFileResultFromArray(fileName, rentedArray, actualCount);
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
    /// IAsyncEnumerableを使用したストリーミング処理（ReadOnlyMemoryによるゼロコピー最適化）
    /// </summary>
    private async Task<FileResult> ProcessFileStreamingAsync(string filePath, IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        var lineSource = _fileSystem.ReadLinesAsMemoryAsync(filePath, cancellationToken);
        return await ProcessCoreStreamingAsync(filePath, lineSource, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 標準入力のストリーミング処理
    /// </summary>
    private async Task<FileResult> ProcessStandardInputStreamingAsync(IMatchStrategy strategy, IOptionContext options, string pattern, bool invertMatch, int? maxCount, CancellationToken cancellationToken)
    {
        const string fileName = "(standard input)";
        var lineSource = _fileSystem.ReadStandardInputAsMemoryAsync(cancellationToken);
        return await ProcessCoreStreamingAsync(fileName, lineSource, strategy, options, pattern, invertMatch, maxCount, cancellationToken).ConfigureAwait(false);
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
