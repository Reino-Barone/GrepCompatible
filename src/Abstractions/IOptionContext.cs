using GrepCompatible.CommandLine;
using GrepCompatible.Constants;

namespace GrepCompatible.Abstractions;

/// <summary>
/// オプションコンテキストのインターフェース
/// コマンドラインオプションと引数の管理とアクセスを提供
/// </summary>
public interface IOptionContext
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
    T? GetOption<T>(OptionNames name) where T : Option;

    /// <summary>
    /// 指定された名前の引数を取得
    /// </summary>
    T? GetArgument<T>(ArgumentNames name) where T : Argument;

    /// <summary>
    /// フラグオプションの値を取得
    /// </summary>
    bool GetFlagValue(OptionNames optionName);

    /// <summary>
    /// 文字列オプションの値を取得
    /// </summary>
    string? GetStringValue(OptionNames optionName);

    /// <summary>
    /// 整数オプションの値を取得
    /// </summary>
    int? GetIntValue(OptionNames optionName);

    /// <summary>
    /// 文字列引数の値を取得
    /// </summary>
    string? GetStringArgumentValue(ArgumentNames argumentName);

    /// <summary>
    /// 文字列リスト引数の値を取得
    /// </summary>
    IReadOnlyList<string>? GetStringListArgumentValue(ArgumentNames argumentName);

    /// <summary>
    /// 指定された名前のオプションの全ての値を取得（複数指定対応）
    /// </summary>
    IReadOnlyList<string> GetAllStringValues(OptionNames optionName);
}