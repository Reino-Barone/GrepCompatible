namespace GrepCompatible.Abstractions;

/// <summary>
/// パフォーマンス最適化の計算を提供するサービス
/// </summary>
public interface IPerformanceOptimizer
{
    /// <summary>
    /// ファイル数に基づいて最適な並列度を計算
    /// </summary>
    /// <param name="fileCount">処理するファイル数</param>
    /// <returns>最適な並列度</returns>
    int CalculateOptimalParallelism(int fileCount);

    /// <summary>
    /// ファイルサイズに応じた最適なバッファサイズを計算
    /// </summary>
    /// <param name="fileSize">ファイルサイズ（バイト）</param>
    /// <returns>最適なバッファサイズ</returns>
    int GetOptimalBufferSize(long fileSize);
}
