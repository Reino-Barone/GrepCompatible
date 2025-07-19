using GrepCompatible.Abstractions;

namespace GrepCompatible.Core;

/// <summary>
/// パフォーマンス最適化の計算サービス実装
/// </summary>
public class PerformanceOptimizer : IPerformanceOptimizer
{
    public int CalculateOptimalParallelism(int fileCount)
    {
        var processorCount = Environment.ProcessorCount;
        
        return fileCount switch
        {
            // MaxDegreeOfParallelismは0にできないため、最小値は1に設定
            <= 0 => 1,
            
            // 小さなファイル数の場合は並列度を制限
            <= 4 => Math.Min(fileCount, processorCount),
            
            // 中程度のファイル数の場合はCPUコア数を使用
            <= 20 => processorCount,
            
            // 大量のファイルの場合は少し並列度を上げる
            _ => Math.Min(processorCount * 2, fileCount)
        };
    }

    public int GetOptimalBufferSize(long fileSize)
    {
        // 小さなファイル（1KB未満）: 1KB
        if (fileSize < 1024)
            return 1024;
        
        // 中程度のファイル（1MB未満）: 4KB
        if (fileSize < 1024 * 1024)
            return 4096;
        
        // 大きなファイル（10MB未満）: 8KB
        if (fileSize < 10 * 1024 * 1024)
            return 8192;
        
        // 非常に大きなファイル: 16KB
        return 16384;
    }
}
