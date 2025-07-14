using GrepCompatible.Models;
using System.Text;

namespace GrepCompatible.Core;

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
    Task<int> FormatOutputAsync(SearchResult result, GrepOptions options, TextWriter writer);
}

/// <summary>
/// POSIX準拠の出力フォーマッター
/// </summary>
public class PosixOutputFormatter : IOutputFormatter
{
    public async Task<int> FormatOutputAsync(SearchResult result, GrepOptions options, TextWriter writer)
    {
        var hasMatches = result.TotalMatches > 0;
        
        // サイレントモードの場合は何も出力しない
        if (options.SilentMode)
            return hasMatches ? 0 : 1;
        
        // カウントのみモード
        if (options.CountOnly)
        {
            await FormatCountOnlyAsync(result, options, writer);
            return hasMatches ? 0 : 1;
        }
        
        // ファイル名のみモード
        if (options.FilenameOnly)
        {
            await FormatFilenameOnlyAsync(result, options, writer);
            return hasMatches ? 0 : 1;
        }
        
        // 通常の出力
        await FormatNormalOutputAsync(result, options, writer);
        return hasMatches ? 0 : 1;
    }

    private async Task FormatCountOnlyAsync(SearchResult result, GrepOptions options, TextWriter writer)
    {
        foreach (var fileResult in result.SuccessfulResults)
        {
            if (options.ShouldShowFilename)
            {
                await writer.WriteAsync($"{fileResult.FileName}:{fileResult.TotalMatches}");
            }
            else
            {
                await writer.WriteAsync(fileResult.TotalMatches.ToString());
            }
            await writer.WriteLineAsync();
        }
    }

    private async Task FormatFilenameOnlyAsync(SearchResult result, GrepOptions options, TextWriter writer)
    {
        foreach (var fileResult in result.SuccessfulResults.Where(r => r.HasMatches))
        {
            await writer.WriteLineAsync(fileResult.FileName);
        }
    }

    private async Task FormatNormalOutputAsync(SearchResult result, GrepOptions options, TextWriter writer)
    {
        foreach (var fileResult in result.SuccessfulResults.Where(r => r.HasMatches))
        {
            await FormatFileResultAsync(fileResult, options, writer);
        }
    }

    private async Task FormatFileResultAsync(FileResult fileResult, GrepOptions options, TextWriter writer)
    {
        var contextBefore = options.BeforeContext;
        var contextAfter = options.AfterContext;
        
        if (contextBefore == 0 && contextAfter == 0)
        {
            // コンテキストなしの場合
            foreach (var match in fileResult.Matches)
            {
                await FormatMatchAsync(match, options, writer);
            }
        }
        else
        {
            // コンテキストありの場合（簡略化実装）
            var groupedMatches = fileResult.Matches.GroupBy(m => m.LineNumber);
            foreach (var group in groupedMatches)
            {
                foreach (var match in group)
                {
                    await FormatMatchAsync(match, options, writer);
                }
            }
        }
    }

    private async Task FormatMatchAsync(MatchResult match, GrepOptions options, TextWriter writer)
    {
        var output = new StringBuilder();
        
        // ファイル名
        if (options.ShouldShowFilename)
        {
            output.Append($"{match.FileName}:");
        }
        
        // 行番号
        if (options.LineNumber)
        {
            output.Append($"{match.LineNumber}:");
        }
        
        // マッチした行
        output.Append(match.Line);
        
        await writer.WriteLineAsync(output.ToString());
    }
}
