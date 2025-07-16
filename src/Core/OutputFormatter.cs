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
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var successfulResults = result.SuccessfulResults.ToList();
        var shouldShowFilename = !suppressFilename && (successfulResults.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        
        foreach (var fileResult in successfulResults)
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
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var successfulResults = result.SuccessfulResults.ToList();
        var shouldShowFilename = !suppressFilename && (successfulResults.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        var showLineNumber = options.GetFlagValue(OptionNames.LineNumber);
        var contextBefore = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextBefore) ?? 0;
        var contextAfter = options.GetIntValue(OptionNames.Context) ?? options.GetIntValue(OptionNames.ContextAfter) ?? 0;
        
        // オプション値をFormatOptionsとして渡す
        var formatOptions = new FormatOptions(shouldShowFilename, showLineNumber, contextBefore, contextAfter);
        
        // LINQ最適化: 一度の反復で処理
        foreach (var fileResult in successfulResults)
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
            // コンテキストありの場合: ArrayPoolを使用してメモリ効率を向上
            var currentLineNumber = -1;
            var rentedArray = ArrayPool<MatchResult>.Shared.Rent(32); // 初期サイズ32
            var currentGroupCount = 0;
            
            try
            {
                foreach (var match in fileResult.Matches)
                {
                    if (match.LineNumber != currentLineNumber)
                    {
                        // 前のグループを処理
                        if (currentGroupCount > 0)
                        {
                            for (int i = 0; i < currentGroupCount; i++)
                            {
                                await FormatMatchAsync(rentedArray[i], formatOptions, writer);
                            }
                            currentGroupCount = 0;
                        }
                        
                        currentLineNumber = match.LineNumber;
                    }
                    
                    // 配列サイズが不足する場合は拡張
                    if (currentGroupCount >= rentedArray.Length)
                    {
                        var newArray = ArrayPool<MatchResult>.Shared.Rent(rentedArray.Length * 2);
                        Array.Copy(rentedArray, newArray, currentGroupCount);
                        ArrayPool<MatchResult>.Shared.Return(rentedArray);
                        rentedArray = newArray;
                    }
                    
                    rentedArray[currentGroupCount++] = match;
                }
                
                // 最後のグループを処理
                if (currentGroupCount > 0)
                {
                    for (int i = 0; i < currentGroupCount; i++)
                    {
                        await FormatMatchAsync(rentedArray[i], formatOptions, writer);
                    }
                }
            }
            finally
            {
                ArrayPool<MatchResult>.Shared.Return(rentedArray);
            }
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
