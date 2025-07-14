using GrepCompatible.CommandLine;
using GrepCompatible.Constants;

namespace GrepCompatible.Models;

/// <summary>
/// 動的オプションクラス - 明示的なプロパティを持たず、List<Option>を使用
/// </summary>
public class DynamicOptions
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
    public T? GetOption<T>(string name) where T : Option
    {
        return _options.OfType<T>().FirstOrDefault(o => o.Name == name);
    }

    /// <summary>
    /// 指定された名前の引数を取得
    /// </summary>
    public T? GetArgument<T>(string name) where T : Argument
    {
        return _arguments.OfType<T>().FirstOrDefault(a => a.Name == name);
    }

    /// <summary>
    /// フラグオプションの値を取得（列挙体版）
    /// </summary>
    public bool GetFlagValue(OptionNames optionName)
    {
        return GetFlagValue(optionName.ToString());
    }

    /// <summary>
    /// フラグオプションの値を取得
    /// </summary>
    public bool GetFlagValue(string name)
    {
        return GetOption<FlagOption>(name)?.Value ?? false;
    }

    /// <summary>
    /// 文字列オプションの値を取得（列挙体版）
    /// </summary>
    public string? GetStringValue(OptionNames optionName)
    {
        return GetStringValue(optionName.ToString());
    }

    /// <summary>
    /// 文字列オプションの値を取得
    /// </summary>
    public string? GetStringValue(string name)
    {
        return GetOption<StringOption>(name)?.Value;
    }

    /// <summary>
    /// 整数オプションの値を取得（列挙体版）
    /// </summary>
    public int? GetIntValue(OptionNames optionName)
    {
        return GetIntValue(optionName.ToString());
    }

    /// <summary>
    /// 整数オプションの値を取得
    /// </summary>
    public int? GetIntValue(string name)
    {
        return GetOption<NullableIntegerOption>(name)?.Value;
    }

    /// <summary>
    /// 文字列引数の値を取得（列挙体版）
    /// </summary>
    public string? GetStringArgumentValue(ArgumentNames argumentName)
    {
        return GetStringArgumentValue(argumentName.ToString());
    }

    /// <summary>
    /// 文字列引数の値を取得
    /// </summary>
    public string? GetStringArgumentValue(string name)
    {
        return GetArgument<StringArgument>(name)?.Value;
    }

    /// <summary>
    /// 文字列リスト引数の値を取得（列挙体版）
    /// </summary>
    public IReadOnlyList<string>? GetStringListArgumentValue(ArgumentNames argumentName)
    {
        return GetStringListArgumentValue(argumentName.ToString());
    }

    /// <summary>
    /// 文字列リスト引数の値を取得
    /// </summary>
    public IReadOnlyList<string>? GetStringListArgumentValue(string name)
    {
        return GetArgument<StringListArgument>(name)?.Value;
    }
}
