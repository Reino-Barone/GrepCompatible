using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
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
            var splitPatterns = singleValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
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
                var splitPatterns = optionValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
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
