using GrepCompatible.Abstractions;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Abstractions.Constants;

namespace GrepCompatible.Abstractions;

/// <summary>
/// オプションコンテキストの実装クラス
/// 明示的なプロパティを持たず、List<Option>とList<Argument>を使用した動的管理
/// </summary>
public class DynamicOptions : IOptionContext
{
    private readonly List<Option> _options = [];
    private readonly List<Argument> _arguments = [];

    /// <summary>
    /// オプションのリスト
    /// </summary>
    public IReadOnlyList<Option> Options => _options.AsReadOnly();

    /// <summary>
    /// 引数のリスト
    /// </summary>
    public IReadOnlyList<Argument> Arguments => _arguments.AsReadOnly();

    /// <summary>
    /// オプションを追加
    /// </summary>
    public void AddOption(Option option)
    {
        _options.Add(option);
    }

    /// <summary>
    /// 引数を追加
    /// </summary>
    public void AddArgument(Argument argument)
    {
        _arguments.Add(argument);
    }

    /// <summary>
    /// 指定された名前のオプションを取得
    /// </summary>
    public T? GetOption<T>(OptionNames name) where T : Option
    {
        return _options.OfType<T>().FirstOrDefault(o => o.Name == name);
    }

    /// <summary>
    /// 指定された名前の引数を取得
    /// </summary>
    public T? GetArgument<T>(ArgumentNames name) where T : Argument
    {
        return _arguments.OfType<T>().FirstOrDefault(a => a.Name == name);
    }

    /// <summary>
    /// フラグオプションの値を取得
    /// </summary>
    public bool GetFlagValue(OptionNames optionName)
    {
        return GetOption<FlagOption>(optionName)?.Value ?? false;
    }

    /// <summary>
    /// 文字列オプションの値を取得
    /// </summary>
    public string? GetStringValue(OptionNames optionName)
    {
        return GetOption<StringOption>(optionName)?.Value;
    }

    /// <summary>
    /// 整数オプションの値を取得
    /// </summary>
    public int? GetIntValue(OptionNames optionName)
    {
        return GetOption<NullableIntegerOption>(optionName)?.Value;
    }

    /// <summary>
    /// 文字列引数の値を取得
    /// </summary>
    public string? GetStringArgumentValue(ArgumentNames argumentName)
    {
        return GetArgument<StringArgument>(argumentName)?.Value;
    }

    /// <summary>
    /// 文字列リスト引数の値を取得
    /// </summary>
    public IReadOnlyList<string>? GetStringListArgumentValue(ArgumentNames argumentName)
    {
        return GetArgument<StringListArgument>(argumentName)?.Value;
    }

    /// <summary>
    /// 指定された名前のオプションの全ての値を取得（複数指定対応）
    /// </summary>
    public IReadOnlyList<string> GetAllStringValues(OptionNames optionName)
    {
        return _options.OfType<StringOption>()
            .Where(o => o.Name == optionName)
            .Select(o => o.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList()
            .AsReadOnly();
    }
}
