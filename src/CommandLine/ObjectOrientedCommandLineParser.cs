using GrepCompatible.Models;

namespace GrepCompatible.CommandLine;

/// <summary>
/// オブジェクト指向コマンドライン解析インターフェース
/// </summary>
public interface ICommandLineParser
{
    /// <summary>
    /// コマンドライン引数を解析
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>解析結果</returns>
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

/// <summary>
/// オブジェクト指向コマンドライン解析の実装
/// </summary>
public class ObjectOrientedCommandLineParser : ICommandLineParser
{
    private readonly GrepCommand _command;
    
    public ObjectOrientedCommandLineParser()
    {
        _command = new GrepCommand();
    }
    
    public ParseResult ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return ParseResult.Error("No arguments provided");
        
        var result = _command.Parse(args);
        
        return result switch
        {
            { IsSuccess: true } => ParseResult.Success(_command.ToGrepOptions()),
            { ShowHelp: true } => ParseResult.Help(),
            { ErrorMessage: not null } => ParseResult.Error(result.ErrorMessage),
            _ => ParseResult.Error("Unknown parsing error")
        };
    }
    
    /// <summary>
    /// ヘルプテキストを取得
    /// </summary>
    public string GetHelpText() => _command.GetHelpText();
}
