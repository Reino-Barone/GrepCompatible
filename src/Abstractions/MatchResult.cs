namespace GrepCompatible.Abstractions;

/// <summary>
/// マッチ結果を表現するレコード
/// </summary>
public record MatchResult(
    string FileName,
    int LineNumber,
    string Line,
    ReadOnlyMemory<char> MatchedText,
    int StartIndex,
    int EndIndex
)
{
    /// <summary>
    /// マッチした部分のスパンを取得
    /// </summary>
    public ReadOnlySpan<char> MatchedSpan => MatchedText.Span;
    
    /// <summary>
    /// マッチした部分の文字列を取得
    /// </summary>
    public override string ToString() => MatchedText.ToString();
}

/// <summary>
/// コンテキスト行を表現するレコード
/// </summary>
public record ContextLine(
    string FileName,
    int LineNumber,
    string Line,
    bool IsMatch
);

/// <summary>
/// コンテキスト付きマッチ結果を表現するレコード
/// </summary>
public record ContextualMatchResult(
    MatchResult Match,
    IReadOnlyList<ContextLine> BeforeContext,
    IReadOnlyList<ContextLine> AfterContext
);

/// <summary>
/// ファイル処理結果を表現するレコード
/// </summary>
public record FileResult(
    string FileName,
    IReadOnlyList<MatchResult> Matches,
    int TotalMatches,
    bool HasError = false,
    string? ErrorMessage = null,
    IReadOnlyList<ContextualMatchResult>? ContextualMatches = null
)
{
    /// <summary>
    /// マッチが存在するかどうか
    /// </summary>
    public bool HasMatches => TotalMatches > 0;
    
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public bool IsSuccess => !HasError;
    
    /// <summary>
    /// コンテキスト情報が利用可能かどうか
    /// </summary>
    public bool HasContextualMatches => ContextualMatches != null && ContextualMatches.Count > 0;
}

/// <summary>
/// 検索結果全体を表現するレコード
/// </summary>
public record SearchResult(
    IReadOnlyList<FileResult> FileResults,
    int TotalMatches,
    int TotalFiles,
    TimeSpan ElapsedTime
)
{
    /// <summary>
    /// 成功したファイル結果のみを取得
    /// </summary>
    public IEnumerable<FileResult> SuccessfulResults => FileResults.Where(r => r.IsSuccess);
    
    /// <summary>
    /// エラーが発生したファイル結果のみを取得
    /// </summary>
    public IEnumerable<FileResult> ErrorResults => FileResults.Where(r => r.HasError);
    
    /// <summary>
    /// 全体的な成功かどうか
    /// </summary>
    public bool IsOverallSuccess => !FileResults.Any(r => r.HasError);
}
