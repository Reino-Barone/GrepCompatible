using GrepCompatible.Models;

namespace GrepCompatible.CommandLine;

/// <summary>
/// パース結果を表現するレコード
/// </summary>
public record ParseResult(
    DynamicOptions? Options,
    bool IsSuccess,
    string? ErrorMessage = null,
    bool ShowHelp = false
)
{
    /// <summary>
    /// 成功したパース結果を作成
    /// </summary>
    public static ParseResult Success(DynamicOptions options) => new(options, true);
    
    /// <summary>
    /// エラーのパース結果を作成
    /// </summary>
    public static ParseResult Error(string message) => new(null, false, message);
    
    /// <summary>
    /// ヘルプ表示のパース結果を作成
    /// </summary>
    public static ParseResult Help() => new(null, false, null, true);
}
