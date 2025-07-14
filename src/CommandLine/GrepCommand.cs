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
        IgnoreCase = GetOption<FlagOption>("IgnoreCase")!;
        InvertMatch = GetOption<FlagOption>("InvertMatch")!;
        LineNumber = GetOption<FlagOption>("LineNumber")!;
        CountOnly = GetOption<FlagOption>("CountOnly")!;
        FilenameOnly = GetOption<FlagOption>("FilenameOnly")!;
        SuppressFilename = GetOption<FlagOption>("SuppressFilename")!;
        SilentMode = GetOption<FlagOption>("SilentMode")!;
        ExtendedRegexp = GetOption<FlagOption>("ExtendedRegexp")!;
        FixedStrings = GetOption<FlagOption>("FixedStrings")!;
        WholeWord = GetOption<FlagOption>("WholeWord")!;
        RecursiveSearch = GetOption<FlagOption>("RecursiveSearch")!;
        ExcludePattern = GetOption<StringOption>("ExcludePattern")!;
        IncludePattern = GetOption<StringOption>("IncludePattern")!;
        MaxCount = GetOption<NullableIntegerOption>("MaxCount")!;
        ContextBefore = GetOption<NullableIntegerOption>("ContextBefore")!;
        ContextAfter = GetOption<NullableIntegerOption>("ContextAfter")!;
        Context = GetOption<NullableIntegerOption>("Context")!;
        
        // 引数の参照を保持
        Pattern = GetArgument<StringArgument>("Pattern")!;
        Files = GetArgument<StringListArgument>("Files")!;
    }
    
    private static IEnumerable<Option> CreateOptions()
    {
        return new Option[]
        {
            new FlagOption("IgnoreCase", "ignore case distinctions", false, "-i", "--ignore-case"),
            new FlagOption("InvertMatch", "select non-matching lines", false, "-v", "--invert-match"),
            new FlagOption("LineNumber", "print line number with output lines", false, "-n", "--line-number"),
            new FlagOption("CountOnly", "print only a count of matching lines per FILE", false, "-c", "--count"),
            new FlagOption("FilenameOnly", "print only names of FILEs containing matches", false, "-l", "--files-with-matches"),
            new FlagOption("SuppressFilename", "suppress the file name prefix on output", false, "-h", "--no-filename"),
            new FlagOption("SilentMode", "suppress all normal output", false, "-q", "--quiet"),
            new FlagOption("ExtendedRegexp", "PATTERN is an extended regular expression", false, "-E", "--extended-regexp"),
            new FlagOption("FixedStrings", "PATTERN is a set of newline-separated fixed strings", false, "-F", "--fixed-strings"),
            new FlagOption("WholeWord", "force PATTERN to match only whole words", false, "-w", "--word-regexp"),
            new FlagOption("RecursiveSearch", "search files under each directory, recursively", false, "-r", "--recursive"),
            new StringOption("ExcludePattern", "skip files that match FILE_PATTERN", "", null, "--exclude"),
            new StringOption("IncludePattern", "search only files that match FILE_PATTERN", "", null, "--include"),
            new NullableIntegerOption("MaxCount", "stop after NUM matches", null, "-m", "--max-count", minValue: 1),
            new NullableIntegerOption("ContextBefore", "print NUM lines of leading context", null, "-B", "--before-context", minValue: 0),
            new NullableIntegerOption("ContextAfter", "print NUM lines of trailing context", null, "-A", "--after-context", minValue: 0),
            new NullableIntegerOption("Context", "print NUM lines of output context", null, "-C", "--context", minValue: 0)
        };
    }
    
    private static IEnumerable<Argument> CreateArguments()
    {
        return new Argument[]
        {
            new StringArgument("Pattern", "Search pattern", "", true),
            new StringListArgument("Files", "Files to search", false)
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
    /// 現在の設定からGrepOptionsを構築
    /// </summary>
    public GrepOptions ToGrepOptions()
    {
        var files = Files.Value.Count > 0 ? Files.Value : new[] { "-" }.ToList().AsReadOnly();
        
        return new GrepOptions(
            Pattern: Pattern.Value,
            Files: files,
            IgnoreCase: IgnoreCase.Value,
            InvertMatch: InvertMatch.Value,
            LineNumber: LineNumber.Value,
            CountOnly: CountOnly.Value,
            FilenameOnly: FilenameOnly.Value,
            SuppressFilename: SuppressFilename.Value,
            SilentMode: SilentMode.Value,
            ExtendedRegexp: ExtendedRegexp.Value,
            FixedStrings: FixedStrings.Value,
            WholeWord: WholeWord.Value,
            RecursiveSearch: RecursiveSearch.Value,
            ExcludePattern: string.IsNullOrEmpty(ExcludePattern.Value) ? null : ExcludePattern.Value,
            IncludePattern: string.IsNullOrEmpty(IncludePattern.Value) ? null : IncludePattern.Value,
            MaxCount: MaxCount.Value,
            ContextBefore: ContextBefore.Value,
            ContextAfter: ContextAfter.Value,
            Context: Context.Value
        );
    }
}
