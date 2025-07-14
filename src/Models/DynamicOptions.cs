using GrepCompatible.CommandLine;

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
    /// フラグオプションの値を取得
    /// </summary>
    public bool GetFlagValue(string name)
    {
        return GetOption<FlagOption>(name)?.Value ?? false;
    }

    /// <summary>
    /// 文字列オプションの値を取得
    /// </summary>
    public string? GetStringValue(string name)
    {
        return GetOption<StringOption>(name)?.Value;
    }

    /// <summary>
    /// 整数オプションの値を取得
    /// </summary>
    public int? GetIntValue(string name)
    {
        return GetOption<NullableIntegerOption>(name)?.Value;
    }

    /// <summary>
    /// 文字列引数の値を取得
    /// </summary>
    public string? GetStringArgumentValue(string name)
    {
        return GetArgument<StringArgument>(name)?.Value;
    }

    /// <summary>
    /// 文字列リスト引数の値を取得
    /// </summary>
    public IReadOnlyList<string>? GetStringListArgumentValue(string name)
    {
        return GetArgument<StringListArgument>(name)?.Value;
    }

    /// <summary>
    /// 従来のGrepOptionsとの互換性のために、よく使用される値を取得するヘルパーメソッド
    /// </summary>
    public class GrepOptionsHelper
    {
        private readonly DynamicOptions _options;

        public GrepOptionsHelper(DynamicOptions options)
        {
            _options = options;
        }

        public string Pattern => _options.GetStringArgumentValue("Pattern") ?? "";
        public IReadOnlyList<string> Files => _options.GetStringListArgumentValue("Files") ?? 
            new[] { "-" }.ToList().AsReadOnly();
        public bool IgnoreCase => _options.GetFlagValue("IgnoreCase");
        public bool InvertMatch => _options.GetFlagValue("InvertMatch");
        public bool LineNumber => _options.GetFlagValue("LineNumber");
        public bool CountOnly => _options.GetFlagValue("CountOnly");
        public bool FilenameOnly => _options.GetFlagValue("FilenameOnly");
        public bool SuppressFilename => _options.GetFlagValue("SuppressFilename");
        public bool SilentMode => _options.GetFlagValue("SilentMode");
        public bool ExtendedRegexp => _options.GetFlagValue("ExtendedRegexp");
        public bool FixedStrings => _options.GetFlagValue("FixedStrings");
        public bool WholeWord => _options.GetFlagValue("WholeWord");
        public bool RecursiveSearch => _options.GetFlagValue("RecursiveSearch");
        public string? ExcludePattern => _options.GetStringValue("ExcludePattern");
        public string? IncludePattern => _options.GetStringValue("IncludePattern");
        public int? MaxCount => _options.GetIntValue("MaxCount");
        public int? ContextBefore => _options.GetIntValue("ContextBefore");
        public int? ContextAfter => _options.GetIntValue("ContextAfter");
        public int? Context => _options.GetIntValue("Context");

        /// <summary>
        /// 実際のコンテキスト前行数を取得
        /// </summary>
        public int BeforeContext => Context ?? ContextBefore ?? 0;
        
        /// <summary>
        /// 実際のコンテキスト後行数を取得
        /// </summary>
        public int AfterContext => Context ?? ContextAfter ?? 0;
        
        /// <summary>
        /// 複数ファイルを処理するかどうか
        /// </summary>
        public bool IsMultiFileMode => Files.Count > 1;
        
        /// <summary>
        /// ファイル名を出力するかどうか
        /// </summary>
        public bool ShouldShowFilename => !SuppressFilename && (IsMultiFileMode || FilenameOnly);
    }

    /// <summary>
    /// GrepOptionsHelperのインスタンスを取得
    /// </summary>
    public GrepOptionsHelper ToGrepOptionsHelper() => new(this);
}

/// <summary>
/// 一時的な互換性のための拡張メソッド
/// </summary>
public static class GrepOptionsHelperExtensions
{
    /// <summary>
    /// GrepOptionsHelperから従来のGrepOptionsを作成（一時的な互換性のため）
    /// </summary>
    public static GrepOptions ToGrepOptions(this DynamicOptions.GrepOptionsHelper helper)
    {
        return new GrepOptions(
            Pattern: helper.Pattern,
            Files: helper.Files,
            IgnoreCase: helper.IgnoreCase,
            InvertMatch: helper.InvertMatch,
            LineNumber: helper.LineNumber,
            CountOnly: helper.CountOnly,
            FilenameOnly: helper.FilenameOnly,
            SuppressFilename: helper.SuppressFilename,
            SilentMode: helper.SilentMode,
            ExtendedRegexp: helper.ExtendedRegexp,
            FixedStrings: helper.FixedStrings,
            WholeWord: helper.WholeWord,
            RecursiveSearch: helper.RecursiveSearch,
            ExcludePattern: helper.ExcludePattern,
            IncludePattern: helper.IncludePattern,
            MaxCount: helper.MaxCount,
            ContextBefore: helper.ContextBefore,
            ContextAfter: helper.ContextAfter,
            Context: helper.Context
        );
    }
}
