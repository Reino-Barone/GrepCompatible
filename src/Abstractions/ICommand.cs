using GrepCompatible.Abstractions.Constants;

namespace GrepCompatible.Abstractions;

/// <summary>
/// コマンドインターフェース
/// </summary>
public interface ICommand
{
    /// <summary>
    /// コマンドライン引数を解析
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>解析結果</returns>
    CommandParseResult Parse(string[] args);
    
    /// <summary>
    /// ヘルプテキストを取得
    /// </summary>
    /// <returns>ヘルプテキスト</returns>
    string GetHelpText();
    
    /// <summary>
    /// オプションコンテキストを取得
    /// </summary>
    /// <returns>オプションコンテキスト</returns>
    IOptionContext ToOptionContext();
}

/// <summary>
/// コマンドの解析結果
/// </summary>
public record CommandParseResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    bool ShowHelp = false
)
{
    public static CommandParseResult Success() => new(true);
    public static CommandParseResult Error(string message) => new(false, message);
    public static CommandParseResult Help() => new(false, null, true);
}
