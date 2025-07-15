using GrepCompatible.Abstractions;
using GrepCompatible.CommandLine;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;

namespace GrepCompatible.Core;

/// <summary>
/// Grepアプリケーションのメインクラス
/// </summary>
public class GrepApplication(
    ICommand command,
    IGrepEngine engine,
    IOutputFormatter formatter)
{
    private readonly ICommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private readonly IGrepEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IOutputFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

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
            if (args.Length == 0)
            {
                await Console.Error.WriteLineAsync("No arguments provided");
                return 2;
            }
            
            var parseResult = _command.Parse(args);
            
            if (parseResult.ShowHelp)
            {
                await Console.Out.WriteLineAsync(_command.GetHelpText());
                return 0;
            }
            
            if (!parseResult.IsSuccess)
            {
                await Console.Error.WriteLineAsync($"Error: {parseResult.ErrorMessage}");
                return 2;
            }

            var optionContext = _command.ToOptionContext();
            var searchResult = await _engine.SearchAsync(optionContext, cancellationToken);

            return await _formatter.FormatOutputAsync(searchResult, optionContext, Console.Out);
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
        var command = new GrepCommand();
        var strategyFactory = new MatchStrategyFactory();
        var fileSystem = new FileSystem();
        var pathHelper = new PathHelper();
        var engine = new ParallelGrepEngine(strategyFactory, fileSystem, pathHelper);
        var formatter = new PosixOutputFormatter();
        
        return new GrepApplication(command, engine, formatter);
    }
}
