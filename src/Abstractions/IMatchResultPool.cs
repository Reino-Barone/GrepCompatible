using GrepCompatible.Core;

namespace GrepCompatible.Abstractions;

/// <summary>
/// MatchResultのArrayPool管理を提供するサービス
/// </summary>
public interface IMatchResultPool
{
    /// <summary>
    /// プールされた配列を取得
    /// </summary>
    /// <param name="estimatedSize">推定サイズ</param>
    /// <returns>プールされた配列情報</returns>
    PooledArray<MatchResult> Rent(int estimatedSize);

    /// <summary>
    /// プールされた配列にマッチを追加
    /// </summary>
    /// <param name="pooledArray">プール配列</param>
    /// <param name="match">追加するマッチ結果</param>
    /// <param name="maxCount">最大カウント制限</param>
    void AddMatch(PooledArray<MatchResult> pooledArray, MatchResult match, int? maxCount = null);

    /// <summary>
    /// プール配列からFileResultを作成
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="pooledArray">プール配列</param>
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
    internal PooledArray(IMatchResultPool pool, T[] array, int size, int count)
    {
        _pool = pool;
        Array = array;
        Size = size;
        Count = count;
    }

    /// <summary>
    /// 内部状態を更新（MatchResultPoolから使用）
    /// </summary>
    internal void UpdateState(T[] array, int size, int count)
    {
        Array = array;
        Size = size;
        Count = count;
    }

    /// <summary>
    /// カウントを増加
    /// </summary>
    internal void IncrementCount()
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
