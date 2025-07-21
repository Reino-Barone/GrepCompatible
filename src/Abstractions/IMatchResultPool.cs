namespace GrepCompatible.Abstractions;

/// <summary>
/// プールから配列をレンタルする際のインターフェース
/// </summary>
public interface IMatchResultPool
{
    /// <summary>
    /// 指定されたサイズで配列をレンタル
    /// </summary>
    /// <param name="estimatedSize">推定サイズ</param>
    /// <returns>プールされた配列</returns>
    PooledArray<MatchResult> Rent(int estimatedSize);

    /// <summary>
    /// プールされた配列にマッチを追加
    /// </summary>
    /// <param name="pooledArray">プールされた配列</param>
    /// <param name="match">追加するマッチ</param>
    /// <param name="maxCount">最大カウント（null許可）</param>
    void AddMatch(PooledArray<MatchResult> pooledArray, MatchResult match, int? maxCount = null);

    /// <summary>
    /// プールされた配列からFileResultを作成
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="pooledArray">プールされた配列</param>
    /// <returns>ファイル結果</returns>
    FileResult CreateFileResult(string fileName, PooledArray<MatchResult> pooledArray);

    /// <summary>
    /// 配列をプールに返却（内部使用）
    /// </summary>
    /// <param name="array">返却する配列</param>
    void ReturnArray<T>(T[] array);
}

/// <summary>
/// ArrayPoolから取得された配列の管理情報
/// </summary>
/// <typeparam name="T">配列の要素型</typeparam>
public class PooledArray<T> : IDisposable
{
    private readonly IMatchResultPool _pool;
    private bool _disposed = false;

    /// <summary>
    /// レンタルされた配列
    /// </summary>
    public T[] Array { get; private set; }
    
    /// <summary>
    /// 配列の現在のサイズ
    /// </summary>
    public int Size { get; private set; }
    
    /// <summary>
    /// 実際に使用されている要素数
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="pool">プールの参照</param>
    /// <param name="array">配列</param>
    /// <param name="size">サイズ</param>
    /// <param name="count">カウント</param>
    public PooledArray(IMatchResultPool pool, T[] array, int size, int count)
    {
        _pool = pool;
        Array = array;
        Size = size;
        Count = count;
    }

    /// <summary>
    /// 内部状態を更新（MatchResultPoolから使用）
    /// </summary>
    public void UpdateState(T[] array, int size, int count)
    {
        Array = array;
        Size = size;
        Count = count;
    }

    /// <summary>
    /// カウントを増加
    /// </summary>
    public void IncrementCount()
    {
        Count++;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (Array != null)
            {
                _pool.ReturnArray(Array);
            }
            _disposed = true;
        }
    }
}