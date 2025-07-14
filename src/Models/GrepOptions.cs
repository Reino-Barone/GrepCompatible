namespace GrepCompatible.Models;

/// <summary>
/// POSIX仕様準拠のGrepオプションを表現するオブジェクト
/// </summary>
public record GrepOptions(
    string Pattern,
    IReadOnlyList<string> Files,
    bool IgnoreCase = false,
    bool InvertMatch = false,
    bool LineNumber = false,
    bool CountOnly = false,
    bool FilenameOnly = false,
    bool SuppressFilename = false,
    bool SilentMode = false,
    bool ExtendedRegexp = false,
    bool FixedStrings = false,
    bool WholeWord = false,
    bool RecursiveSearch = false,
    string? ExcludePattern = null,
    string? IncludePattern = null,
    int? MaxCount = null,
    int? ContextBefore = null,
    int? ContextAfter = null,
    int? Context = null
)
{
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
