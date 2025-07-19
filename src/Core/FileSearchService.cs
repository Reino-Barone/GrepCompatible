using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Models;
using System.Buffers;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace GrepCompatible.Core;

/// <summary>
/// ファイル探索とパターンマッチングのサービス実装
/// </summary>
public class FileSearchService(IFileSystem fileSystem, IPath pathHelper) : IFileSearchService
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IPath _pathHelper = pathHelper ?? throw new ArgumentNullException(nameof(pathHelper));
    
    private static readonly string[] SourceArray = ["-"];
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    
    // 正規表現の特殊文字セット（.NET 8+のSearchValues使用）
    private static readonly SearchValues<char> RegexSpecialChars = SearchValues.Create([
        '.', '^', '$', '(', ')', '[', ']', '{', '}', '|', '\\', '+', '*', '?'
    ]);

    public async Task<IEnumerable<string>> ExpandFilesAsync(IOptionContext options, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        var filesArg = options.GetStringListArgumentValue(ArgumentNames.Files) ??
            SourceArray.ToList().AsReadOnly();
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

    public bool ShouldIncludeFile(string filePath, IOptionContext options)
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
    /// 複数パターンのマッチングを実行
    /// </summary>
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

    /// <summary>
    /// オプションから複数パターンを取得
    /// </summary>
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

    /// <summary>
    /// SearchValuesを使用したglobパターン変換
    /// </summary>
    private static string ConvertGlobToRegex(string globPattern)
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
            int nextSpecialIndex = pattern[i..].IndexOfAny(RegexSpecialChars);
            
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
    private static Regex GetCompiledRegex(string pattern, bool isGlobPattern)
    {
        var key = isGlobPattern ? $"glob:{pattern}" : $"regex:{pattern}";
        
        return RegexCache.GetOrAdd(key, _ =>
        {
            var regexPattern = isGlobPattern ? ConvertGlobToRegex(pattern) : pattern;
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
    }
}
