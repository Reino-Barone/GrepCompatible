using GrepCompatible.Abstractions;
using System.Buffers;

namespace GrepCompatible.Core;

/// <summary>
/// MatchResultのArrayPool管理サービス実装
/// </summary>
public class MatchResultPool : IMatchResultPool
{
    private static readonly ArrayPool<MatchResult> Pool = ArrayPool<MatchResult>.Shared;

    public PooledArray<MatchResult> Rent(int estimatedSize)
    {
        var array = Pool.Rent(estimatedSize);
        return new PooledArray<MatchResult>(this, array, estimatedSize, 0);
    }

    public void AddMatch(PooledArray<MatchResult> pooledArray, MatchResult match, int? maxCount = null)
    {
        // 配列のリサイズが必要かチェック
        var resized = ResizeArrayIfNeeded(pooledArray.Array, pooledArray.Count, pooledArray.Size, maxCount ?? 0);
        
        if (resized.array != pooledArray.Array)
        {
            // リサイズが発生した場合、新しい配列情報で更新
            pooledArray.UpdateState(resized.array, resized.newSize, pooledArray.Count);
        }
        
        // マッチを配列に追加
        pooledArray.Array[pooledArray.Count] = match;
        pooledArray.IncrementCount();
    }

    public FileResult CreateFileResult(string fileName, PooledArray<MatchResult> pooledArray)
    {
        var results = new MatchResult[pooledArray.Count];
        Array.Copy(pooledArray.Array, results, pooledArray.Count);
        return new FileResult(fileName, results.AsReadOnly(), pooledArray.Count);
    }

    public void ReturnArray<T>(T[] array)
    {
        if (array is MatchResult[] matchResultArray)
        {
            Pool.Return(matchResultArray, clearArray: true);
        }
    }

    /// <summary>
    /// ArrayPoolを使用した動的配列管理
    /// </summary>
    private static (MatchResult[] array, int newSize) ResizeArrayIfNeeded(MatchResult[] currentArray, int currentCount, int currentSize, int maxCount)
    {
        // 最大数制限がある場合は現在の配列を使用
        if (maxCount > 0 && currentCount >= maxCount)
            return (currentArray, currentSize);
        
        // 配列がフルになった場合は拡張
        if (currentCount >= currentSize)
        {
            var newSize = Math.Min(currentSize * 2, maxCount > 0 ? maxCount : currentSize * 2);
            var newArray = Pool.Rent(newSize);
            Array.Copy(currentArray, newArray, currentCount);
            Pool.Return(currentArray, clearArray: true);
            return (newArray, newSize);
        }
        
        return (currentArray, currentSize);
    }
}
