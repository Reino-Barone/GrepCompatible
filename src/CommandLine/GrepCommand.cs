using GrepCompatible.Constants;
using GrepCompatible.Models;

namespace GrepCompatible.CommandLine;

/// <summary>
/// Grepコマンドの具体実装
/// </summary>
public class GrepCommand : Command
{
    // オプション定義
    public FlagOption IgnoreCase { get; }
    public FlagOption InvertMatch { get; }
    public FlagOption LineNumber { get; }
    public FlagOption CountOnly { get; }
    public FlagOption FilenameOnly { get; }
    public FlagOption SuppressFilename { get; }
    public FlagOption SilentMode { get; }
    public FlagOption ExtendedRegexp { get; }
    public FlagOption FixedStrings { get; }
    public FlagOption WholeWord { get; }
    public FlagOption RecursiveSearch { get; }
    public StringOption ExcludePattern { get; }
    public StringOption IncludePattern { get; }
    public NullableIntegerOption MaxCount { get; }
    public NullableIntegerOption ContextBefore { get; }
    public NullableIntegerOption ContextAfter { get; }
    public NullableIntegerOption Context { get; }
    
    // 引数定義
    public StringArgument Pattern { get; }
    public StringListArgument Files { get; }
    
    public GrepCommand() : base(
        "grep",
        "Search for PATTERN in each FILE.",
        CreateOptions(),
        CreateArguments())
    {
        // オプションの参照を保持
        IgnoreCase = GetOption<FlagOption>(OptionNames.IgnoreCase.ToString())!;
        InvertMatch = GetOption<FlagOption>(OptionNames.InvertMatch.ToString())!;
        LineNumber = GetOption<FlagOption>(OptionNames.LineNumber.ToString())!;
        CountOnly = GetOption<FlagOption>(OptionNames.CountOnly.ToString())!;
        FilenameOnly = GetOption<FlagOption>(OptionNames.FilenameOnly.ToString())!;
        SuppressFilename = GetOption<FlagOption>(OptionNames.SuppressFilename.ToString())!;
        SilentMode = GetOption<FlagOption>(OptionNames.SilentMode.ToString())!;
        ExtendedRegexp = GetOption<FlagOption>(OptionNames.ExtendedRegexp.ToString())!;
        FixedStrings = GetOption<FlagOption>(OptionNames.FixedStrings.ToString())!;
        WholeWord = GetOption<FlagOption>(OptionNames.WholeWord.ToString())!;
        RecursiveSearch = GetOption<FlagOption>(OptionNames.RecursiveSearch.ToString())!;
        ExcludePattern = GetOption<StringOption>(OptionNames.ExcludePattern.ToString())!;
        IncludePattern = GetOption<StringOption>(OptionNames.IncludePattern.ToString())!;
        MaxCount = GetOption<NullableIntegerOption>(OptionNames.MaxCount.ToString())!;
        ContextBefore = GetOption<NullableIntegerOption>(OptionNames.ContextBefore.ToString())!;
        ContextAfter = GetOption<NullableIntegerOption>(OptionNames.ContextAfter.ToString())!;
        Context = GetOption<NullableIntegerOption>(OptionNames.Context.ToString())!;
        
        // 引数の参照を保持
        Pattern = GetArgument<StringArgument>(ArgumentNames.Pattern.ToString())!;
        Files = GetArgument<StringListArgument>(ArgumentNames.Files.ToString())!;;;
    }
    
    private static IEnumerable<Option> CreateOptions()
    {
        return new Option[]
        {
            new FlagOption(OptionNames.IgnoreCase.ToString(), "ignore case distinctions", false, "-i", "--ignore-case"),
            new FlagOption(OptionNames.InvertMatch.ToString(), "select non-matching lines", false, "-v", "--invert-match"),
            new FlagOption(OptionNames.LineNumber.ToString(), "print line number with output lines", false, "-n", "--line-number"),
            new FlagOption(OptionNames.CountOnly.ToString(), "print only a count of matching lines per FILE", false, "-c", "--count"),
            new FlagOption(OptionNames.FilenameOnly.ToString(), "print only names of FILEs containing matches", false, "-l", "--files-with-matches"),
            new FlagOption(OptionNames.SuppressFilename.ToString(), "suppress the file name prefix on output", false, "-h", "--no-filename"),
            new FlagOption(OptionNames.SilentMode.ToString(), "suppress all normal output", false, "-q", "--quiet"),
            new FlagOption(OptionNames.ExtendedRegexp.ToString(), "PATTERN is an extended regular expression", false, "-E", "--extended-regexp"),
            new FlagOption(OptionNames.FixedStrings.ToString(), "PATTERN is a set of newline-separated fixed strings", false, "-F", "--fixed-strings"),
            new FlagOption(OptionNames.WholeWord.ToString(), "force PATTERN to match only whole words", false, "-w", "--word-regexp"),
            new FlagOption(OptionNames.RecursiveSearch.ToString(), "search files under each directory, recursively", false, "-r", "--recursive"),
            new StringOption(OptionNames.ExcludePattern.ToString(), "skip files that match FILE_PATTERN", "", null, "--exclude"),
            new StringOption(OptionNames.IncludePattern.ToString(), "search only files that match FILE_PATTERN", "", null, "--include"),
            new NullableIntegerOption(OptionNames.MaxCount.ToString(), "stop after NUM matches", null, "-m", "--max-count", minValue: 1),
            new NullableIntegerOption(OptionNames.ContextBefore.ToString(), "print NUM lines of leading context", null, "-B", "--before-context", minValue: 0),
            new NullableIntegerOption(OptionNames.ContextAfter.ToString(), "print NUM lines of trailing context", null, "-A", "--after-context", minValue: 0),
            new NullableIntegerOption(OptionNames.Context.ToString(), "print NUM lines of output context", null, "-C", "--context", minValue: 0)
        };
    }
    
    private static IEnumerable<Argument> CreateArguments()
    {
        return new Argument[]
        {
            new StringArgument(ArgumentNames.Pattern.ToString(), "Search pattern", "", true),
            new StringListArgument(ArgumentNames.Files.ToString(), "Files to search", false)
        };
    }
    
    private T? GetOption<T>(string name) where T : Option
    {
        return Options.OfType<T>().FirstOrDefault(o => o.Name == name);
    }
    
    private T? GetArgument<T>(string name) where T : Argument
    {
        return Arguments.OfType<T>().FirstOrDefault(a => a.Name == name);
    }
    
    /// <summary>
    /// 現在の設定からDynamicOptionsを構築
    /// </summary>
    public DynamicOptions ToDynamicOptions()
    {
        var dynamicOptions = new DynamicOptions();
        
        // オプションを追加
        foreach (var option in Options)
        {
            dynamicOptions.AddOption(option);
        }
        
        // 引数を追加
        foreach (var argument in Arguments)
        {
            dynamicOptions.AddArgument(argument);
        }
        
        return dynamicOptions;
    }
}
