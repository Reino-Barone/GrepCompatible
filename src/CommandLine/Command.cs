using GrepCompatible.Constants;
using GrepCompatible.Models;

namespace GrepCompatible.CommandLine;

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

/// <summary>
/// コマンドの抽象基底クラス
/// </summary>
public abstract class Command
{
    /// <summary>
    /// コマンド名
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// コマンドの説明
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// オプションのリスト
    /// </summary>
    public IReadOnlyList<Option> Options { get; }

    /// <summary>
    /// 引数のリスト
    /// </summary>
    public IReadOnlyList<Argument> Arguments { get; }

    /// <summary>
    /// オプション名からオプションへのマッピング（文字列キー）
    /// </summary>
    private readonly Dictionary<string, Option> _optionMap = [];

    /// <summary>
    /// オプション名からオプションへのマッピング（列挙体キー）
    /// </summary>
    private readonly Dictionary<OptionNames, Option> _optionNameMap = [];

    /// <summary>
    /// 引数のインデックス
    /// </summary>
    private int _argumentIndex = 0;

    protected Command(string name, string description, IEnumerable<Option> options, IEnumerable<Argument> arguments)
    {
        Name = name;
        Description = description;
        Options = options.ToArray();
        Arguments = arguments.ToArray();

        // オプションマッピングを構築
        foreach (var option in Options)
        {
            if (option.ShortName != null)
                _optionMap[option.ShortName] = option;
            if (option.LongName != null)
                _optionMap[option.LongName] = option;

            // 列挙体キーでもマッピング
            _optionNameMap[option.Name] = option;
        }
    }

    /// <summary>
    /// コマンドライン引数を解析
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>解析結果</returns>
    public CommandParseResult Parse(string[] args)
    {
        Reset();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith('-'))
            {
                var result = ParseOption(arg, args, ref i);
                if (!result.IsSuccess)
                    return result;

                if (result.ShowHelp)
                    return result;
            }
            else
            {
                var result = ParseArgument(arg);
                if (!result.IsSuccess)
                    return result;
            }
        }

        // 必須オプションと引数のチェック
        var validationResult = ValidateRequiredItems();
        if (!validationResult.IsSuccess)
            return validationResult;

        return CommandParseResult.Success();
    }

    /// <summary>
    /// オプションを解析
    /// </summary>
    private CommandParseResult ParseOption(string arg, string[] args, ref int index)
    {
        // ヘルプオプションの特別処理
        if (arg is "-?" or "--help")
            return CommandParseResult.Help();

        // オプション名と値を分離
        string optionName;
        string? optionValue = null;

        if (arg.Contains('='))
        {
            var parts = arg.Split('=', 2);
            optionName = parts[0];
            optionValue = parts[1];
        }
        else
        {
            optionName = arg;
            // 次の引数が値かどうかをチェック
            if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
            {
                var option = _optionMap.GetValueOrDefault(optionName);
                if (option != null && option is not FlagOption)
                {
                    optionValue = args[++index];
                }
            }
        }

        if (!_optionMap.TryGetValue(optionName, out var targetOption))
            return CommandParseResult.Error($"Unknown option: {optionName}");

        if (!targetOption.TryParse(optionValue))
            return CommandParseResult.Error($"Invalid value for option {optionName}: {optionValue}");

        return CommandParseResult.Success();
    }

    /// <summary>
    /// 引数を解析
    /// </summary>
    private CommandParseResult ParseArgument(string value)
    {
        // 最後の引数がリスト引数の場合、すべて追加
        if (_argumentIndex >= Arguments.Count)
        {
            var lastArg = Arguments.LastOrDefault();
            if (lastArg is StringListArgument listArg)
            {
                if (!listArg.TryParse(value))
                    return CommandParseResult.Error($"Invalid value for argument {lastArg.Name}: {value}");
                return CommandParseResult.Success();
            }

            return CommandParseResult.Error($"Too many arguments provided");
        }

        var argument = Arguments[_argumentIndex];
        if (!argument.TryParse(value))
            return CommandParseResult.Error($"Invalid value for argument {argument.Name}: {value}");

        // リスト引数でない場合は次の引数に進む
        if (argument is not StringListArgument)
            _argumentIndex++;

        return CommandParseResult.Success();
    }

    /// <summary>
    /// 必須項目を検証
    /// </summary>
    private CommandParseResult ValidateRequiredItems()
    {
        // 必須オプションのチェック
        foreach (var option in Options.Where(o => o.IsRequired && !o.IsSet))
        {
            return CommandParseResult.Error($"Required option is missing: {option.Name}");
        }

        // 必須引数のチェック
        foreach (var argument in Arguments.Where(a => a.IsRequired && !a.IsSet))
        {
            return CommandParseResult.Error($"Required argument is missing: {argument.Name}");
        }

        return CommandParseResult.Success();
    }

    /// <summary>
    /// オプションと引数をリセット
    /// </summary>
    private void Reset()
    {
        foreach (var option in Options)
            option.Reset();

        foreach (var argument in Arguments)
            argument.Reset();

        _argumentIndex = 0;
    }

    /// <summary>
    /// 使用方法文字列を生成
    /// </summary>
    public virtual string GetUsageString()
    {
        var usage = new List<string> { Name };

        // オプションを追加
        foreach (var option in Options)
        {
            var optionString = option.GetUsageString();
            if (!option.IsRequired)
                optionString = $"[{optionString}]";
            usage.Add(optionString);
        }

        // 引数を追加
        foreach (var argument in Arguments)
        {
            usage.Add(argument.GetUsageString());
        }

        return string.Join(" ", usage);
    }

    /// <summary>
    /// ヘルプテキストを生成
    /// </summary>
    public virtual string GetHelpText()
    {
        var help = new List<string>
        {
            $"Usage: {GetUsageString()}",
            "",
            Description
        };

        if (Options.Any())
        {
            help.Add("");
            help.Add("Options:");
            foreach (var option in Options)
            {
                help.Add($"  {option.GetUsageString(),-30} {option.Description}");
            }
        }

        if (Arguments.Any())
        {
            help.Add("");
            help.Add("Arguments:");
            foreach (var argument in Arguments)
            {
                help.Add($"  {argument.GetUsageString(),-30} {argument.Description}");
            }
        }

        return string.Join(Environment.NewLine, help);
    }

    /// <summary>
    /// オプション名（列挙体）でオプションを取得
    /// </summary>
    /// <typeparam name="T">オプションの型</typeparam>
    /// <param name="name">オプション名</param>
    /// <returns>オプション</returns>
    protected T? GetOption<T>(OptionNames name) where T : Option
    {
        return _optionNameMap.TryGetValue(name, out var option) ? option as T : null;
    }

    /// <summary>
    /// 引数名（列挙体）で引数を取得
    /// </summary>
    /// <typeparam name="T">引数の型</typeparam>
    /// <param name="name">引数名</param>
    /// <returns>引数</returns>
    protected T? GetArgument<T>(ArgumentNames name) where T : Argument
    {
        return Arguments.OfType<T>().FirstOrDefault(a => a.Name == name);
    }

    public abstract IOptionContext ToOptionContext();
}
