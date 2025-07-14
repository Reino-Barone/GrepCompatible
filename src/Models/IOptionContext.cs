using GrepCompatible.CommandLine;
using GrepCompatible.Constants;

namespace GrepCompatible.Models;

/// <summary>
/// 動的オプションクラスのインターフェース
/// </summary>
public interface IDynamicOptions
{
    /// <summary>
    /// オプションのリスト
    /// </summary>
    IReadOnlyList<Option> Options { get; }

    /// <summary>
    /// 引数のリスト
    /// </summary>
    IReadOnlyList<Argument> Arguments { get; }

    /// <summary>
    /// オプションを追加
    /// </summary>
    void AddOption(Option option);

    /// <summary>
    /// 引数を追加
    /// </summary>
    void AddArgument(Argument argument);

    /// <summary>
    /// 指定された名前のオプションを取得
    /// </summary>
    T? GetOption<T>(string name) where T : Option;

    /// <summary>
    /// 指定された名前の引数を取得
    /// </summary>
    T? GetArgument<T>(string name) where T : Argument;

    /// <summary>
    /// フラグオプションの値を取得（列挙体版）
    /// </summary>
    bool GetFlagValue(OptionNames optionName);

    /// <summary>
    /// フラグオプションの値を取得
    /// </summary>
    bool GetFlagValue(string name);

    /// <summary>
    /// 文字列オプションの値を取得（列挙体版）
    /// </summary>
    string? GetStringValue(OptionNames optionName);

    /// <summary>
    /// 文字列オプションの値を取得
    /// </summary>
    string? GetStringValue(string name);

    /// <summary>
    /// 整数オプションの値を取得（列挙体版）
    /// </summary>
    int? GetIntValue(OptionNames optionName);

    /// <summary>
    /// 整数オプションの値を取得
    /// </summary>
    int? GetIntValue(string name);

    /// <summary>
    /// 文字列引数の値を取得（列挙体版）
    /// </summary>
    string? GetStringArgumentValue(ArgumentNames argumentName);

    /// <summary>
    /// 文字列引数の値を取得
    /// </summary>
    string? GetStringArgumentValue(string name);

    /// <summary>
    /// 文字列リスト引数の値を取得（列挙体版）
    /// </summary>
    IReadOnlyList<string>? GetStringListArgumentValue(ArgumentNames argumentName);

    /// <summary>
    /// 文字列リスト引数の値を取得
    /// </summary>
    IReadOnlyList<string>? GetStringListArgumentValue(string name);
}
