using GrepCompatible.CommandLine;
using GrepCompatible.Core;
using GrepCompatible.Parsers;
using GrepCompatible.Strategies;

namespace GrepCompatible.Core;

/// <summary>
/// Grepアプリケーションのメインクラス
/// </summary>
public class GrepApplication
{
    private readonly Parsers.ICommandLineParser _parser;
    private readonly IGrepEngine _engine;
    private readonly IOutputFormatter _formatter;

    public GrepApplication(
        Parsers.ICommandLineParser parser,
        IGrepEngine engine,
        IOutputFormatter formatter)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    /// <summary>
    /// アプリケーションを実行
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>終了コード</returns>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var parseResult = _parser.ParseArguments(args);
            
            if (parseResult.ShowHelp)
            {
                var helpText = _parser switch
                {
                    CommandLineParserAdapter adapter => adapter.GetHelpText(),
                    PosixCommandLineParser => PosixCommandLineParser.GetHelpText(),
                    _ => "Help not available"
                };
                await Console.Out.WriteLineAsync(helpText);
                return 0;
            }
            
            if (!parseResult.IsSuccess)
            {
                await Console.Error.WriteLineAsync($"Error: {parseResult.ErrorMessage}");
                return 2;
            }
            
            var options = parseResult.Options!;
            var searchResult = await _engine.SearchAsync(options, cancellationToken);
            
            return await _formatter.FormatOutputAsync(searchResult, options, Console.Out);
        }
        catch (OperationCanceledException)
        {
            return 130; // SIGINT終了コード
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// デフォルトの設定でアプリケーションを作成
    /// </summary>
    /// <returns>設定済みのアプリケーション</returns>
    public static GrepApplication CreateDefault()
    {
        var parser = new CommandLineParserAdapter();
        var strategyFactory = new MatchStrategyFactory();
        var engine = new ParallelGrepEngine(strategyFactory);
        var formatter = new PosixOutputFormatter();
        
        return new GrepApplication(parser, engine, formatter);
    }
    
    /// <summary>
    /// 従来のパーサーを使用してアプリケーションを作成
    /// </summary>
    /// <returns>設定済みのアプリケーション</returns>
    public static GrepApplication CreateWithLegacyParser()
    {
        var parser = new PosixCommandLineParser();
        var strategyFactory = new MatchStrategyFactory();
        var engine = new ParallelGrepEngine(strategyFactory);
        var formatter = new PosixOutputFormatter();
        
        return new GrepApplication(parser, engine, formatter);
    }
}
