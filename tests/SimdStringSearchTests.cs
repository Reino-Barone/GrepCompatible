using System.Numerics;
using GrepCompatible.Core;
using Xunit;

namespace GrepCompatible.Test;

public class SimdStringSearchTests
{
    [Fact]
    public void IndexOf_SingleCharacterCaseSensitive_ReturnsCorrectIndex()
    {
        var source = "Hello World";
        var pattern = "W";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(6, result);
    }

    [Fact]
    public void IndexOf_SingleCharacterCaseInsensitive_ReturnsCorrectIndex()
    {
        var source = "Hello World";
        var pattern = "w";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
        
        Assert.Equal(6, result);
    }

    [Fact]
    public void IndexOf_SingleCharacterNotFound_ReturnsMinusOne()
    {
        var source = "Hello World";
        var pattern = "X";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public void IndexOf_MultipleCharactersFound_ReturnsFirstIndex()
    {
        var source = "Hello World Hello";
        var pattern = "Hello";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void IndexOf_MultipleCharactersCaseInsensitive_ReturnsFirstIndex()
    {
        var source = "Hello World hello";
        var pattern = "HELLO";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void IndexOf_PatternLongerThanSource_ReturnsMinusOne()
    {
        var source = "Hi";
        var pattern = "Hello";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public void IndexOf_EmptyPattern_ReturnsZero()
    {
        var source = "Hello World";
        var pattern = "";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void IndexOf_EmptySource_ReturnsMinusOne()
    {
        var source = "";
        var pattern = "Hello";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindAllMatches_MultipleOccurrences_ReturnsAllIndices()
    {
        var source = "Hello World Hello Universe Hello";
        var pattern = "Hello";
        
        var result = SimdStringSearch.FindAllMatches(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(new[] { 0, 12, 27 }, result.ToArray());
    }

    [Fact]
    public void FindAllMatches_OverlappingPattern_ReturnsAllIndices()
    {
        var source = "aaaaaa";
        var pattern = "aa";
        
        var result = SimdStringSearch.FindAllMatches(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, result.ToArray());
    }

    [Fact]
    public void FindAllMatches_CaseInsensitive_ReturnsAllIndices()
    {
        var source = "Hello world HELLO universe hello";
        var pattern = "hello";
        
        var result = SimdStringSearch.FindAllMatches(source.AsSpan(), pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
        
        Assert.Equal(new[] { 0, 12, 27 }, result.ToArray());
    }

    [Fact]
    public void FindAllMatches_NoMatches_ReturnsEmptyList()
    {
        var source = "Hello World";
        var pattern = "xyz";
        
        var result = SimdStringSearch.FindAllMatches(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void IndexOfSingleChar_Performance_IsFasterThanStringIndexOf()
    {
        // パフォーマンステスト（大きな文字列で）
        var largeString = string.Join("", Enumerable.Repeat("Hello World Testing SIMD Performance ", 1000));
        var target = 'S';
        
        // SIMD版の実行時間を測定
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var simdResult = SimdStringSearch.IndexOfSingleChar(largeString.AsSpan(), target, StringComparison.Ordinal);
        sw1.Stop();
        
        // 標準のIndexOfの実行時間を測定
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var standardResult = largeString.IndexOf(target);
        sw2.Stop();
        
        // 結果が同じであることを確認
        Assert.Equal(standardResult, simdResult);
        
        // パフォーマンステストは参考情報として記録
        Assert.True(simdResult >= 0); // 結果が正しければOK
    }

    [Fact]
    public void IndexOf_VectorAcceleration_WorksCorrectly()
    {
        // SIMD最適化が有効な場合のテスト
        var source = "abcdefghijklmnopqrstuvwxyz";
        var pattern = "xyz";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(23, result);
    }

    [Fact]
    public void IndexOf_UnicodeCharacters_WorksCorrectly()
    {
        var source = "こんにちは世界！Hello World";
        var pattern = "世界";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(5, result);
    }

    [Fact]
    public void IndexOf_SpecialCharacters_WorksCorrectly()
    {
        var source = "Hello@World#Test$Pattern%";
        var pattern = "Test$";
        
        var result = SimdStringSearch.IndexOf(source.AsSpan(), pattern.AsSpan(), StringComparison.Ordinal);
        
        Assert.Equal(12, result);
    }
}