using GrepCompatible.Models;

namespace GrepCompatible.Parsers;

/// <summary>
/// コマンドライン引数パーサーのインターフェース
/// </summary>
public interface ICommandLineParser
{
    /// <summary>
    /// コマンドライン引数をパース
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>パース結果</returns>
    ParseResult ParseArguments(string[] args);
}

/// <summary>
/// パース結果を表現するレコード
/// </summary>
public record ParseResult(
    GrepOptions? Options,
    bool IsSuccess,
    string? ErrorMessage = null,
    bool ShowHelp = false
)
{
    /// <summary>
    /// 成功したパース結果を作成
    /// </summary>
    public static ParseResult Success(GrepOptions options) => new(options, true);
    
    /// <summary>
    /// エラーのパース結果を作成
    /// </summary>
    public static ParseResult Error(string message) => new(null, false, message);
    
    /// <summary>
    /// ヘルプ表示のパース結果を作成
    /// </summary>
    public static ParseResult Help() => new(null, false, null, true);
}
