using GrepCompatible.Abstractions;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Core.Strategies;

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
                await Console.Error.WriteLineAsync("No arguments provided").ConfigureAwait(false);
                return 2;
            }
            
            var parseResult = _command.Parse(args);
            
            if (parseResult.ShowHelp)
            {
                await Console.Out.WriteLineAsync(_command.GetHelpText()).ConfigureAwait(false);
                return 0;
            }
            
            if (!parseResult.IsSuccess)
            {
                await Console.Error.WriteLineAsync($"Error: {parseResult.ErrorMessage}").ConfigureAwait(false);
                return 2;
            }

            var optionContext = _command.ToOptionContext();
            var searchResult = await _engine.SearchAsync(optionContext, cancellationToken).ConfigureAwait(false);

            return await _formatter.FormatOutputAsync(searchResult, optionContext, Console.Out).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 130; // SIGINT終了コード
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"Stack trace: {ex.StackTrace}").ConfigureAwait(false);
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
        
        // 新しいサービスを作成
        var fileSearchService = new FileSearchService(fileSystem, pathHelper);
        var performanceOptimizer = new PerformanceOptimizer();
        var matchResultPool = new MatchResultPool();
        
        var engine = new ParallelGrepEngine(
            strategyFactory, 
            fileSystem, 
            pathHelper,
            fileSearchService,
            performanceOptimizer,
            matchResultPool);
        var formatter = new PosixOutputFormatter();
        
        return new GrepApplication(command, engine, formatter);
    }
}
