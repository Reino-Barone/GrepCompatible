using GrepCompatible.Parsers;

namespace GrepCompatible.CommandLine;

/// <summary>
/// 新しいオブジェクト指向パーサーを古いインターフェースに適応するアダプター
/// </summary>
public class CommandLineParserAdapter : Parsers.ICommandLineParser
{
    private readonly ObjectOrientedCommandLineParser _parser;
    
    public CommandLineParserAdapter()
    {
        _parser = new ObjectOrientedCommandLineParser();
    }
    
    public Parsers.ParseResult ParseArguments(string[] args)
    {
        var result = _parser.ParseArguments(args);
        
        return result switch
        {
            { IsSuccess: true, Options: not null } => Parsers.ParseResult.Success(result.Options),
            { ShowHelp: true } => Parsers.ParseResult.Help(),
            { ErrorMessage: not null } => Parsers.ParseResult.Error(result.ErrorMessage),
            _ => Parsers.ParseResult.Error("Unknown parsing error")
        };
    }
    
    /// <summary>
    /// ヘルプテキストを取得
    /// </summary>
    public string GetHelpText() => _parser.GetHelpText();
}
