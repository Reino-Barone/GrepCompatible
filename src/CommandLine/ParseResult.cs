using GrepCompatible.Abstractions;
using GrepCompatible.Core;

namespace GrepCompatible.CommandLine;

/// <summary>
/// パース結果を表現するレコード
/// </summary>
public record ParseResult(
    IOptionContext? Options,
    bool IsSuccess,
    string? ErrorMessage = null,
    bool ShowHelp = false
)
{
    /// <summary>
    /// 成功したパース結果を作成
    /// </summary>
    public static ParseResult Success(IOptionContext options) => new(options, true);
    
    /// <summary>
    /// エラーのパース結果を作成
    /// </summary>
    public static ParseResult Error(string message) => new(null, false, message);
    
    /// <summary>
    /// ヘルプ表示のパース結果を作成
    /// </summary>
    public static ParseResult Help() => new(null, false, null, true);
}
