using GrepCompatible.Models;

namespace GrepCompatible.Parsers;

/// <summary>
/// POSIX仕様準拠のコマンドライン引数パーサー
/// </summary>
public class PosixCommandLineParser : ICommandLineParser
{
    private const string HelpText = """
        Usage: grep [OPTION]... PATTERN [FILE]...
        Search for PATTERN in each FILE.

        Pattern selection and interpretation:
          -E, --extended-regexp     PATTERN is an extended regular expression
          -F, --fixed-strings       PATTERN is a set of newline-separated fixed strings
          -i, --ignore-case         ignore case distinctions
          -w, --word-regexp         force PATTERN to match only whole words
          -x, --line-regexp         force PATTERN to match only whole lines

        Matching control:
          -v, --invert-match        select non-matching lines
          -m, --max-count=NUM       stop after NUM matches

        General output control:
          -c, --count               print only a count of matching lines per FILE
          -l, --files-with-matches  print only names of FILEs containing matches
          -L, --files-without-match print only names of FILEs containing no match
          -n, --line-number         print line number with output lines
          -H, --with-filename       print the file name for each match
          -h, --no-filename         suppress the file name prefix on output
          -o, --only-matching       show only the part of a line matching PATTERN
          -q, --quiet, --silent     suppress all normal output
          -s, --no-messages         suppress error messages

        Context line control:
          -A, --after-context=NUM   print NUM lines of trailing context
          -B, --before-context=NUM  print NUM lines of leading context
          -C, --context=NUM         print NUM lines of output context

        File and directory selection:
          -r, --recursive           search files under each directory, recursively
          -R, --dereference-recursive  likewise, but follow all symlinks
              --include=FILE_PATTERN    search only files that match FILE_PATTERN
              --exclude=FILE_PATTERN    skip files that match FILE_PATTERN

        Other options:
          -?, --help                display this help and exit
          -V, --version             output version information and exit
        """;

    public ParseResult ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return ParseResult.Error("No arguments provided");

        var options = new GrepOptionsBuilder();
        var files = new List<string>();
        string? pattern = null;
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            if (arg.StartsWith('-'))
            {
                var parseResult = ParseOption(arg, args, ref i, options);
                if (!parseResult.IsSuccess)
                    return parseResult;
                if (parseResult.ShowHelp)
                    return ParseResult.Help();
            }
            else
            {
                // 最初の非オプション引数がパターン
                pattern ??= arg;
                if (pattern != arg)
                    files.Add(arg);
            }
        }

        if (pattern == null)
            return ParseResult.Error("No pattern specified");

        // ファイルが指定されていない場合は標準入力を使用
        if (files.Count == 0)
            files.Add("-");

        try
        {
            var grepOptions = options.Build(pattern, files);
            return ParseResult.Success(grepOptions);
        }
        catch (ArgumentException ex)
        {
            return ParseResult.Error(ex.Message);
        }
    }

    private ParseResult ParseOption(string arg, string[] args, ref int index, GrepOptionsBuilder options)
    {
        return arg switch
        {
            "-E" or "--extended-regexp" => ProcessFlag(() => options.ExtendedRegexp = true),
            "-F" or "--fixed-strings" => ProcessFlag(() => options.FixedStrings = true),
            "-i" or "--ignore-case" => ProcessFlag(() => options.IgnoreCase = true),
            "-w" or "--word-regexp" => ProcessFlag(() => options.WholeWord = true),
            "-v" or "--invert-match" => ProcessFlag(() => options.InvertMatch = true),
            "-c" or "--count" => ProcessFlag(() => options.CountOnly = true),
            "-l" or "--files-with-matches" => ProcessFlag(() => options.FilenameOnly = true),
            "-n" or "--line-number" => ProcessFlag(() => options.LineNumber = true),
            "-H" or "--with-filename" => ProcessFlag(() => options.SuppressFilename = false),
            "-h" or "--no-filename" => ProcessFlag(() => options.SuppressFilename = true),
            "-q" or "--quiet" or "--silent" => ProcessFlag(() => options.SilentMode = true),
            "-r" or "--recursive" => ProcessFlag(() => options.RecursiveSearch = true),
            "-?" or "--help" => new ParseResult(null, false, null, true),
            
            var opt when opt.StartsWith("-m") => ParseNumericOption(opt, "--max-count", args, ref index, val => options.MaxCount = val),
            var opt when opt.StartsWith("-A") => ParseNumericOption(opt, "--after-context", args, ref index, val => options.ContextAfter = val),
            var opt when opt.StartsWith("-B") => ParseNumericOption(opt, "--before-context", args, ref index, val => options.ContextBefore = val),
            var opt when opt.StartsWith("-C") => ParseNumericOption(opt, "--context", args, ref index, val => options.Context = val),
            var opt when opt.StartsWith("--include=") => ParseStringOption(opt, "--include", val => options.IncludePattern = val),
            var opt when opt.StartsWith("--exclude=") => ParseStringOption(opt, "--exclude", val => options.ExcludePattern = val),
            
            _ => ParseResult.Error($"Unknown option: {arg}")
        };
    }

    private static ParseResult ProcessFlag(Action action)
    {
        action();
        return new ParseResult(null, true);
    }

    private ParseResult ParseNumericOption(string arg, string longOption, string[] args, ref int index, Action<int> setter)
    {
        string? valueStr = null;
        
        if (arg.StartsWith(longOption + "="))
        {
            valueStr = arg[(longOption.Length + 1)..];
        }
        else if (arg.Length > 2 && arg[2] != '-')
        {
            valueStr = arg[2..];
        }
        else if (index + 1 < args.Length)
        {
            valueStr = args[++index];
        }
        
        if (valueStr == null)
            return ParseResult.Error($"Option {longOption} requires a value");
        
        if (!int.TryParse(valueStr, out var value) || value < 0)
            return ParseResult.Error($"Invalid numeric value for {longOption}: {valueStr}");
        
        setter(value);
        return new ParseResult(null, true);
    }

    private static ParseResult ParseStringOption(string arg, string optionName, Action<string> setter)
    {
        var value = arg[(optionName.Length + 1)..];
        setter(value);
        return new ParseResult(null, true);
    }

    /// <summary>
    /// ヘルプテキストを取得
    /// </summary>
    public static string GetHelpText() => HelpText;
}

/// <summary>
/// GrepOptionsを構築するためのビルダークラス
/// </summary>
internal class GrepOptionsBuilder
{
    public bool IgnoreCase { get; set; }
    public bool InvertMatch { get; set; }
    public bool LineNumber { get; set; }
    public bool CountOnly { get; set; }
    public bool FilenameOnly { get; set; }
    public bool SuppressFilename { get; set; }
    public bool SilentMode { get; set; }
    public bool ExtendedRegexp { get; set; }
    public bool FixedStrings { get; set; }
    public bool WholeWord { get; set; }
    public bool RecursiveSearch { get; set; }
    public string? ExcludePattern { get; set; }
    public string? IncludePattern { get; set; }
    public int? MaxCount { get; set; }
    public int? ContextBefore { get; set; }
    public int? ContextAfter { get; set; }
    public int? Context { get; set; }

    public GrepOptions Build(string pattern, List<string> files)
    {
        return new GrepOptions(
            Pattern: pattern,
            Files: files.AsReadOnly(),
            IgnoreCase: IgnoreCase,
            InvertMatch: InvertMatch,
            LineNumber: LineNumber,
            CountOnly: CountOnly,
            FilenameOnly: FilenameOnly,
            SuppressFilename: SuppressFilename,
            SilentMode: SilentMode,
            ExtendedRegexp: ExtendedRegexp,
            FixedStrings: FixedStrings,
            WholeWord: WholeWord,
            RecursiveSearch: RecursiveSearch,
            ExcludePattern: ExcludePattern,
            IncludePattern: IncludePattern,
            MaxCount: MaxCount,
            ContextBefore: ContextBefore,
            ContextAfter: ContextAfter,
            Context: Context
        );
    }
}
