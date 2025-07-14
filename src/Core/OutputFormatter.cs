using GrepCompatible.Constants;
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
    Task<int> FormatOutputAsync(SearchResult result, IDynamicOptions options, TextWriter writer);
}

/// <summary>
/// POSIX準拠の出力フォーマッター
/// </summary>
public class PosixOutputFormatter : IOutputFormatter
{
    public async Task<int> FormatOutputAsync(SearchResult result, IDynamicOptions options, TextWriter writer)
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

    private async Task FormatCountOnlyAsync(SearchResult result, IDynamicOptions options, TextWriter writer)
    {
        var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new[] { "-" }.ToList().AsReadOnly();
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var shouldShowFilename = !suppressFilename && (files.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        
        foreach (var fileResult in result.SuccessfulResults)
        {
            if (shouldShowFilename)
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

    private async Task FormatFilenameOnlyAsync(SearchResult result, IDynamicOptions options, TextWriter writer)
    {
        foreach (var fileResult in result.SuccessfulResults.Where(r => r.HasMatches))
        {
            await writer.WriteLineAsync(fileResult.FileName);
        }
    }

    private async Task FormatNormalOutputAsync(SearchResult result, IDynamicOptions options, TextWriter writer)
    {
        foreach (var fileResult in result.SuccessfulResults.Where(r => r.HasMatches))
        {
            await FormatFileResultAsync(fileResult, options, writer);
        }
    }

    private async Task FormatFileResultAsync(FileResult fileResult, IDynamicOptions options, TextWriter writer)
    {
        var contextBefore = options.GetIntValue("Context") ?? options.GetIntValue("ContextBefore") ?? 0;
        var contextAfter = options.GetIntValue("Context") ?? options.GetIntValue("ContextAfter") ?? 0;
        
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

    private async Task FormatMatchAsync(MatchResult match, IDynamicOptions options, TextWriter writer)
    {
        var output = new StringBuilder();
        
        var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new[] { "-" }.ToList().AsReadOnly();
        var suppressFilename = options.GetFlagValue(OptionNames.SuppressFilename);
        var shouldShowFilename = !suppressFilename && (files.Count > 1 || options.GetFlagValue(OptionNames.FilenameOnly));
        
        // ファイル名
        if (shouldShowFilename)
        {
            output.Append($"{match.FileName}:");
        }
        
        // 行番号
        if (options.GetFlagValue(OptionNames.LineNumber))
        {
            output.Append($"{match.LineNumber}:");
        }
        
        // マッチした行
        output.Append(match.Line);
        
        await writer.WriteLineAsync(output.ToString());
    }
}
