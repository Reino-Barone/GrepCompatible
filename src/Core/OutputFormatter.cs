using GrepCompatible.Constants;
using GrepCompatible.Models;
using System.Buffers;
using System.Text;

namespace GrepCompatible.Core;

/// <summary>
/// フォーマットオプションをキャッシュするためのレコード
/// </summary>
/// <param name="ShouldShowFilename">ファイル名を表示するかどうか</param>
/// <param name="ShowLineNumber">行番号を表示するかどうか</param>
/// <param name="ContextBefore">前のコンテキスト行数</param>
/// <param name="ContextAfter">後のコンテキスト行数</param>
public readonly record struct FormatOptions(
    bool ShouldShowFilename,
    bool ShowLineNumber,
    int ContextBefore,
    int ContextAfter
);

/// <summary>
/// 出力フォーマッターのインターフェース
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// 検索結果を出力
    /// </summary>
    /// <param name="result">検索結果</param>
    /// <param name="options">検索オプション</param>
    /// <param name="writer">出力ライター</param>
    /// <returns>終了コード</returns>
    Task<int> FormatOutputAsync(SearchResult result, IOptionContext options, TextWriter writer);
}

/// <summary>
/// POSIX準拠の出力フォーマッター
/// </summary>
public class PosixOutputFormatter : IOutputFormatter
{
    public async Task<int> FormatOutputAsync(SearchResult result, IOptionContext options, TextWriter writer)
    {
        var hasMatches = result.TotalMatches > 0;
        
        // サイレントモードの場合は何も出力しない
        if (options.GetFlagValue(OptionNames.SilentMode))
            return hasMatches ? 0 : 1;
        
        // カウントのみモード
        if (options.GetFlagValue(OptionNames.CountOnly))
        {
            await FormatCountOnlyAsync(result, options, writer);
            return hasMatches ? 0 : 1;
        }
        
        // ファイル名のみモード
        if (options.GetFlagValue(OptionNames.FilenameOnly))
        {
            await FormatFilenameOnlyAsync(result, options, writer);
            return hasMatches ? 0 : 1;
        }
        
        // 通常の出力
        await FormatNormalOutputAsync(result, options, writer);
        return hasMatches ? 0 : 1;
    }

    private async Task FormatCountOnlyAsync(SearchResult result, IOptionContext options, TextWriter writer)
    {
        // オプション値をキャッシュ
        var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new[] { "-" }.ToList().AsReadOnly();
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var shouldShowFilename = !suppressFilename && (files.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        
        foreach (var fileResult in result.SuccessfulResults)
        {
            if (shouldShowFilename)
            {
                // 2個の文字列結合: 直接結合の方が高速
                await writer.WriteLineAsync($"{fileResult.FileName}:{fileResult.TotalMatches}");
            }
            else
            {
                // 単一の値: 直接出力
                await writer.WriteLineAsync(fileResult.TotalMatches.ToString());
            }
        }
    }

    private async Task FormatFilenameOnlyAsync(SearchResult result, IOptionContext options, TextWriter writer)
    {
        // LINQ最適化: 一度の反復で処理
        foreach (var fileResult in result.SuccessfulResults)
        {
            if (fileResult.HasMatches)
            {
                await writer.WriteLineAsync(fileResult.FileName);
            }
        }
    }

    private async Task FormatNormalOutputAsync(SearchResult result, IOptionContext options, TextWriter writer)
    {
        // オプション値を一度だけ取得してキャッシュ
        var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new[] { "-" }.ToList().AsReadOnly();
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var shouldShowFilename = !suppressFilename && (files.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        var showLineNumber = options.GetFlagValue(OptionNames.LineNumber);
        var contextBefore = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextBefore) ?? 0;
        var contextAfter = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextAfter) ?? 0;
        
        // オプション値をFormatOptionsとして渡す
        var formatOptions = new FormatOptions(shouldShowFilename, showLineNumber, contextBefore, contextAfter);
        
        // LINQ最適化: 一度の反復で処理
        foreach (var fileResult in result.SuccessfulResults)
        {
            if (fileResult.HasMatches)
            {
                await FormatFileResultAsync(fileResult, formatOptions, writer);
            }
        }
    }

    private async Task FormatFileResultAsync(FileResult fileResult, FormatOptions formatOptions, TextWriter writer)
    {
        if (formatOptions.ContextBefore == 0 && formatOptions.ContextAfter == 0)
        {
            // コンテキストなしの場合: 直接処理
            foreach (var match in fileResult.Matches)
            {
                await FormatMatchAsync(match, formatOptions, writer);
            }
        }
        else
        {
            // コンテキストありの場合: ContextualMatchesを使用
            if (fileResult.HasContextualMatches)
            {
                await FormatContextualMatchesAsync(fileResult.ContextualMatches!, formatOptions, writer);
            }
            else
            {
                // フォールバック: 通常の処理
                foreach (var match in fileResult.Matches)
                {
                    await FormatMatchAsync(match, formatOptions, writer);
                }
            }
        }
    }

    private async Task FormatContextualMatchesAsync(IReadOnlyList<ContextualMatchResult> contextualMatches, FormatOptions formatOptions, TextWriter writer)
    {
        var processedLines = new HashSet<int>();
        var lastProcessedLine = 0;
        
        foreach (var contextualMatch in contextualMatches)
        {
            var match = contextualMatch.Match;
            
            // 重複する行の処理を避けるために、前回処理した行より後の行のみを処理
            var startLineNumber = Math.Max(lastProcessedLine + 1, 
                contextualMatch.BeforeContext.FirstOrDefault()?.LineNumber ?? match.LineNumber);
            
            // Before context
            foreach (var contextLine in contextualMatch.BeforeContext)
            {
                if (contextLine.LineNumber >= startLineNumber && !processedLines.Contains(contextLine.LineNumber))
                {
                    await FormatContextLineAsync(contextLine, formatOptions, writer);
                    processedLines.Add(contextLine.LineNumber);
                }
            }
            
            // Match line
            if (!processedLines.Contains(match.LineNumber))
            {
                await FormatMatchAsync(match, formatOptions, writer);
                processedLines.Add(match.LineNumber);
            }
            
            // After context
            foreach (var contextLine in contextualMatch.AfterContext)
            {
                if (!processedLines.Contains(contextLine.LineNumber))
                {
                    await FormatContextLineAsync(contextLine, formatOptions, writer);
                    processedLines.Add(contextLine.LineNumber);
                }
            }
            
            lastProcessedLine = Math.Max(lastProcessedLine, 
                contextualMatch.AfterContext.LastOrDefault()?.LineNumber ?? match.LineNumber);
            
            // コンテキストグループの区切り線を追加（複数のマッチがある場合）
            if (contextualMatch != contextualMatches.Last())
            {
                await writer.WriteLineAsync("--");
            }
        }
    }

    private async Task FormatContextLineAsync(ContextLine contextLine, FormatOptions formatOptions, TextWriter writer)
    {
        // コンテキスト行のフォーマット（マッチ行と同じ形式だが、":"の代わりに"-"を付ける）
        if (formatOptions.ShouldShowFilename && formatOptions.ShowLineNumber)
        {
            await writer.WriteLineAsync($"{contextLine.FileName}-{contextLine.LineNumber}-{contextLine.Line}");
        }
        else if (formatOptions.ShouldShowFilename)
        {
            await writer.WriteLineAsync($"{contextLine.FileName}-{contextLine.Line}");
        }
        else if (formatOptions.ShowLineNumber)
        {
            await writer.WriteLineAsync($"{contextLine.LineNumber}-{contextLine.Line}");
        }
        else
        {
            await writer.WriteLineAsync(contextLine.Line);
        }
    }

    private async Task FormatMatchAsync(MatchResult match, FormatOptions formatOptions, TextWriter writer)
    {
        // 小さな文字列結合の場合は直接結合の方が高速
        if (!formatOptions.ShouldShowFilename && !formatOptions.ShowLineNumber)
        {
            // 行のみの場合: 直接出力
            await writer.WriteLineAsync(match.Line);
            return;
        }
        
        // 2-3個の文字列結合の場合は直接結合を使用
        if (formatOptions.ShouldShowFilename && formatOptions.ShowLineNumber)
        {
            // ファイル名 + 行番号 + 行
            await writer.WriteLineAsync($"{match.FileName}:{match.LineNumber}:{match.Line}");
        }
        else if (formatOptions.ShouldShowFilename)
        {
            // ファイル名 + 行
            await writer.WriteLineAsync($"{match.FileName}:{match.Line}");
        }
        else // formatOptions.ShowLineNumber
        {
            // 行番号 + 行
            await writer.WriteLineAsync($"{match.LineNumber}:{match.Line}");
        }
    }
}
