using GrepCompatible.Abstractions;
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
        IgnoreCase = GetOption<FlagOption>(OptionNames.IgnoreCase)!;
        InvertMatch = GetOption<FlagOption>(OptionNames.InvertMatch)!;
        LineNumber = GetOption<FlagOption>(OptionNames.LineNumber)!;
        CountOnly = GetOption<FlagOption>(OptionNames.CountOnly)!;
        FilenameOnly = GetOption<FlagOption>(OptionNames.FilenameOnly)!;
        SuppressFilename = GetOption<FlagOption>(OptionNames.SuppressFilename)!;
        SilentMode = GetOption<FlagOption>(OptionNames.SilentMode)!;
        ExtendedRegexp = GetOption<FlagOption>(OptionNames.ExtendedRegexp)!;
        FixedStrings = GetOption<FlagOption>(OptionNames.FixedStrings)!;
        WholeWord = GetOption<FlagOption>(OptionNames.WholeWord)!;
        RecursiveSearch = GetOption<FlagOption>(OptionNames.RecursiveSearch)!;
        ExcludePattern = GetOption<StringOption>(OptionNames.ExcludePattern)!;
        IncludePattern = GetOption<StringOption>(OptionNames.IncludePattern)!;
        MaxCount = GetOption<NullableIntegerOption>(OptionNames.MaxCount)!;
        ContextBefore = GetOption<NullableIntegerOption>(OptionNames.ContextBefore)!;
        ContextAfter = GetOption<NullableIntegerOption>(OptionNames.ContextAfter)!;
        Context = GetOption<NullableIntegerOption>(OptionNames.Context)!;
        
        // 引数の参照を保持
        Pattern = GetArgument<StringArgument>(ArgumentNames.Pattern)!;
        Files = GetArgument<StringListArgument>(ArgumentNames.Files)!;
    }
    
    private static IEnumerable<Option> CreateOptions()
    {
        return new Option[]
        {
            new FlagOption(OptionNames.IgnoreCase, "ignore case distinctions", false, "-i", "--ignore-case"),
            new FlagOption(OptionNames.InvertMatch, "select non-matching lines", false, "-v", "--invert-match"),
            new FlagOption(OptionNames.LineNumber, "print line number with output lines", false, "-n", "--line-number"),
            new FlagOption(OptionNames.CountOnly, "print only a count of matching lines per FILE", false, "-c", "--count"),
            new FlagOption(OptionNames.FilenameOnly, "print only names of FILEs containing matches", false, "-l", "--files-with-matches"),
            new FlagOption(OptionNames.SuppressFilename, "suppress the file name prefix on output", false, "-h", "--no-filename"),
            new FlagOption(OptionNames.SilentMode, "suppress all normal output", false, "-q", "--quiet"),
            new FlagOption(OptionNames.ExtendedRegexp, "PATTERN is an extended regular expression", false, "-E", "--extended-regexp"),
            new FlagOption(OptionNames.FixedStrings, "PATTERN is a set of newline-separated fixed strings", false, "-F", "--fixed-strings"),
            new FlagOption(OptionNames.WholeWord, "force PATTERN to match only whole words", false, "-w", "--word-regexp"),
            new FlagOption(OptionNames.RecursiveSearch, "search files under each directory, recursively", false, "-r", "--recursive"),
            new StringOption(OptionNames.ExcludePattern, "skip files that match FILE_PATTERN", "", null, "--exclude"),
            new StringOption(OptionNames.IncludePattern, "search only files that match FILE_PATTERN", "", null, "--include"),
            new NullableIntegerOption(OptionNames.MaxCount, "stop after NUM matches", null, "-m", "--max-count", minValue: 1),
            new NullableIntegerOption(OptionNames.ContextBefore, "print NUM lines of leading context", null, "-B", "--before-context", minValue: 0),
            new NullableIntegerOption(OptionNames.ContextAfter, "print NUM lines of trailing context", null, "-A", "--after-context", minValue: 0),
            new NullableIntegerOption(OptionNames.Context, "print NUM lines of output context", null, "-C", "--context", minValue: 0)
        };
    }
    
    private static IEnumerable<Argument> CreateArguments()
    {
        return new Argument[]
        {
            new StringArgument(ArgumentNames.Pattern, "Search pattern", "", true),
            new StringListArgument(ArgumentNames.Files, "Files to search", false)
        };
    }
    
    /// <summary>
    /// 現在の設定からDynamicOptionsを構築
    /// </summary>
    public override IOptionContext ToOptionContext()
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
