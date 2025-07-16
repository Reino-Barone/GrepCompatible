using System.Diagnostics;
using System.Numerics;
using GrepCompatible.Core;
using Xunit;

namespace GrepCompatible.Test;

public class SimdPerformanceTests
{
    [Fact]
    public void SIMD_Performance_Test_SingleCharacter()
    {
        // 大きな文字列を作成
        var largeString = string.Join("", Enumerable.Repeat("Hello World Testing SIMD Performance Optimization ", 5000));
        var target = 'S';
        
        // SIMD版のベンチマーク
        var sw1 = Stopwatch.StartNew();
        var simdResult = SimdStringSearch.IndexOfSingleChar(largeString.AsSpan(), target, StringComparison.Ordinal);
        sw1.Stop();
        
        // 標準版のベンチマーク
        var sw2 = Stopwatch.StartNew();
        var standardResult = largeString.IndexOf(target);
        sw2.Stop();
        
        // 結果が同じであることを確認
        Assert.Equal(standardResult, simdResult);
        
        // パフォーマンス情報を出力
        Console.WriteLine($"Text length: {largeString.Length:N0} characters");
        Console.WriteLine($"Vector.IsHardwareAccelerated: {Vector.IsHardwareAccelerated}");
        Console.WriteLine($"Vector<ushort>.Count: {Vector<ushort>.Count}");
        Console.WriteLine($"SIMD IndexOfSingleChar: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"Standard IndexOf: {sw2.ElapsedMilliseconds}ms");
        
        if (sw2.ElapsedMilliseconds > 0)
        {
            var speedup = (double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds;
            Console.WriteLine($"Speedup: {speedup:F2}x");
        }
    }

    [Fact]
    public void SIMD_Performance_Test_MultipleCharacters()
    {
        // 大きな文字列を作成
        var largeString = string.Join("", Enumerable.Repeat("Hello World Testing SIMD Performance Optimization ", 5000));
        var pattern = "SIMD";
        
        // SIMD版のベンチマーク
        var sw1 = Stopwatch.StartNew();
        var simdResult = SimdStringSearch.IndexOf(largeString.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        sw1.Stop();
        
        // 標準版のベンチマーク
        var sw2 = Stopwatch.StartNew();
        var standardResult = largeString.IndexOf(pattern);
        sw2.Stop();
        
        // 結果が同じであることを確認
        Assert.Equal(standardResult, simdResult);
        
        // パフォーマンス情報を出力
        Console.WriteLine($"Text length: {largeString.Length:N0} characters");
        Console.WriteLine($"Pattern: '{pattern}'");
        Console.WriteLine($"SIMD IndexOf: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"Standard IndexOf: {sw2.ElapsedMilliseconds}ms");
        
        if (sw2.ElapsedMilliseconds > 0)
        {
            var speedup = (double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds;
            Console.WriteLine($"Speedup: {speedup:F2}x");
        }
    }

    [Fact]
    public void SIMD_Performance_Test_FindAllMatches()
    {
        // 繰り返しパターンを含む文字列
        var largeString = string.Join(" ", Enumerable.Repeat("test", 10000));
        var pattern = "test";
        
        // SIMD版のベンチマーク
        var sw1 = Stopwatch.StartNew();
        var simdMatches = SimdStringSearch.FindAllMatches(largeString.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        sw1.Stop();
        
        // 標準版のベンチマーク（手動実装）
        var sw2 = Stopwatch.StartNew();
        var standardMatches = new List<int>();
        int index = 0;
        while ((index = largeString.IndexOf(pattern, index)) != -1)
        {
            standardMatches.Add(index);
            index++;
        }
        sw2.Stop();
        
        // 結果が同じであることを確認
        Assert.Equal(standardMatches.Count, simdMatches.Count);
        Assert.Equal(standardMatches, simdMatches);
        
        // パフォーマンス情報を出力
        Console.WriteLine($"Text length: {largeString.Length:N0} characters");
        Console.WriteLine($"Pattern: '{pattern}'");
        Console.WriteLine($"Matches found: {simdMatches.Count}");
        Console.WriteLine($"SIMD FindAllMatches: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"Standard method: {sw2.ElapsedMilliseconds}ms");
        
        if (sw2.ElapsedMilliseconds > 0)
        {
            var speedup = (double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds;
            Console.WriteLine($"Speedup: {speedup:F2}x");
        }
    }

    [Fact]
    public void SIMD_Performance_Test_CaseInsensitive()
    {
        // 大きな文字列を作成
        var largeString = string.Join("", Enumerable.Repeat("Hello World Testing SIMD performance optimization ", 5000));
        var pattern = "PERFORMANCE";
        
        // SIMD版のベンチマーク
        var sw1 = Stopwatch.StartNew();
        var simdResult = SimdStringSearch.IndexOf(largeString.AsSpan(), pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
        sw1.Stop();
        
        // 標準版のベンチマーク
        var sw2 = Stopwatch.StartNew();
        var standardResult = largeString.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        sw2.Stop();
        
        // 結果が同じであることを確認
        Assert.Equal(standardResult, simdResult);
        
        // パフォーマンス情報を出力
        Console.WriteLine($"Text length: {largeString.Length:N0} characters");
        Console.WriteLine($"Pattern (case-insensitive): '{pattern}'");
        Console.WriteLine($"SIMD IndexOf: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"Standard IndexOf: {sw2.ElapsedMilliseconds}ms");
        
        if (sw2.ElapsedMilliseconds > 0)
        {
            var speedup = (double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds;
            Console.WriteLine($"Speedup: {speedup:F2}x");
        }
    }
}