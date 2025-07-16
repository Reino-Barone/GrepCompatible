using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace GrepCompatible.Core;

/// <summary>
/// SIMD最適化された文字列検索ユーティリティクラス
/// </summary>
public static class SimdStringSearch
{
    /// <summary>
    /// SIMD命令を使用して固定文字列パターンを検索する
    /// </summary>
    /// <param name="source">検索対象の文字列</param>
    /// <param name="pattern">検索パターン</param>
    /// <param name="comparison">文字列比較方法</param>
    /// <returns>マッチした位置のインデックス（見つからない場合は-1）</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOf(ReadOnlySpan<char> source, ReadOnlySpan<char> pattern, StringComparison comparison)
    {
        if (pattern.Length == 0)
            return 0;
        
        if (source.Length < pattern.Length)
            return -1;
        
        // パターンが短い場合は従来の方法を使用
        if (pattern.Length == 1)
        {
            return IndexOfSingleChar(source, pattern[0], comparison);
        }
        
        // パターンが長い場合はSIMD最適化を使用
        if (pattern.Length >= 2 && Vector.IsHardwareAccelerated)
        {
            return comparison == StringComparison.OrdinalIgnoreCase
                ? IndexOfPatternIgnoreCase(source, pattern)
                : IndexOfPatternCaseSensitive(source, pattern);
        }
        
        // フォールバック: 標準的な文字列検索
        return source.ToString().IndexOf(pattern.ToString(), comparison);
    }

    /// <summary>
    /// SIMD命令を使用して単一文字を検索する
    /// </summary>
    /// <param name="source">検索対象の文字列</param>
    /// <param name="target">検索する文字</param>
    /// <param name="comparison">文字列比較方法</param>
    /// <returns>マッチした位置のインデックス（見つからない場合は-1）</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOfSingleChar(ReadOnlySpan<char> source, char target, StringComparison comparison)
    {
        if (source.Length == 0)
            return -1;
        
        if (Vector.IsHardwareAccelerated && source.Length >= Vector<ushort>.Count)
        {
            return comparison == StringComparison.OrdinalIgnoreCase
                ? IndexOfSingleCharIgnoreCase(source, target)
                : IndexOfSingleCharCaseSensitive(source, target);
        }
        
        // フォールバック: 標準的な文字検索
        return source.ToString().IndexOf(target, comparison);
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別しない単一文字検索
    /// </summary>
    private static int IndexOfSingleCharIgnoreCase(ReadOnlySpan<char> source, char target)
    {
        var lowerTarget = char.ToLowerInvariant(target);
        var upperTarget = char.ToUpperInvariant(target);
        
        if (lowerTarget == upperTarget)
        {
            return IndexOfSingleCharCaseSensitive(source, target);
        }
        
        var lowerVector = new Vector<ushort>(lowerTarget);
        var upperVector = new Vector<ushort>(upperTarget);
        
        int vectorSize = Vector<ushort>.Count;
        int i = 0;
        
        // SIMDを使用してベクトル単位で処理
        for (; i <= source.Length - vectorSize; i += vectorSize)
        {
            var sourceVector = CreateVectorFromSpan(source.Slice(i, vectorSize));
            var lowerMatches = Vector.Equals(sourceVector, lowerVector);
            var upperMatches = Vector.Equals(sourceVector, upperVector);
            var matches = Vector.BitwiseOr(lowerMatches, upperMatches);
            
            if (!Vector.EqualsAll(matches, Vector<ushort>.Zero))
            {
                // マッチした要素のインデックスを特定
                for (int j = 0; j < vectorSize; j++)
                {
                    if (matches[j] != 0)
                    {
                        return i + j;
                    }
                }
            }
        }
        
        // 残りの要素を個別に処理
        for (; i < source.Length; i++)
        {
            if (char.ToLowerInvariant(source[i]) == lowerTarget)
            {
                return i;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別する単一文字検索
    /// </summary>
    private static int IndexOfSingleCharCaseSensitive(ReadOnlySpan<char> source, char target)
    {
        var targetVector = new Vector<ushort>(target);
        int vectorSize = Vector<ushort>.Count;
        int i = 0;
        
        // SIMDを使用してベクトル単位で処理
        for (; i <= source.Length - vectorSize; i += vectorSize)
        {
            var sourceVector = CreateVectorFromSpan(source.Slice(i, vectorSize));
            var matches = Vector.Equals(sourceVector, targetVector);
            
            if (!Vector.EqualsAll(matches, Vector<ushort>.Zero))
            {
                // マッチした要素のインデックスを特定
                for (int j = 0; j < vectorSize; j++)
                {
                    if (matches[j] != 0)
                    {
                        return i + j;
                    }
                }
            }
        }
        
        // 残りの要素を個別に処理
        for (; i < source.Length; i++)
        {
            if (source[i] == target)
            {
                return i;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別しないパターン検索
    /// </summary>
    private static int IndexOfPatternIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> pattern)
    {
        var firstChar = pattern[0];
        var lowerFirst = char.ToLowerInvariant(firstChar);
        var upperFirst = char.ToUpperInvariant(firstChar);
        
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            // 最初の文字をSIMDで検索
            int candidateIndex = i;
            var remaining = source.Slice(candidateIndex);
            
            int nextIndex = lowerFirst == upperFirst
                ? IndexOfSingleCharCaseSensitive(remaining, firstChar)
                : IndexOfSingleCharIgnoreCase(remaining, firstChar);
            
            if (nextIndex == -1)
                break;
            
            candidateIndex += nextIndex;
            
            // 残りの文字をSIMDで比較
            if (EqualsIgnoreCase(source.Slice(candidateIndex, pattern.Length), pattern))
            {
                return candidateIndex;
            }
            
            i = candidateIndex;
        }
        
        return -1;
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別するパターン検索
    /// </summary>
    private static int IndexOfPatternCaseSensitive(ReadOnlySpan<char> source, ReadOnlySpan<char> pattern)
    {
        var firstChar = pattern[0];
        
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            // 最初の文字をSIMDで検索
            int candidateIndex = i;
            var remaining = source.Slice(candidateIndex);
            
            int nextIndex = IndexOfSingleCharCaseSensitive(remaining, firstChar);
            
            if (nextIndex == -1)
                break;
            
            candidateIndex += nextIndex;
            
            // 残りの文字をSIMDで比較
            if (EqualsCaseSensitive(source.Slice(candidateIndex, pattern.Length), pattern))
            {
                return candidateIndex;
            }
            
            i = candidateIndex;
        }
        
        return -1;
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別しない文字列比較
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length)
            return false;
        
        if (Vector.IsHardwareAccelerated && left.Length >= Vector<ushort>.Count)
        {
            return EqualsIgnoreCaseSimd(left, right);
        }
        
        // フォールバック
        return left.ToString().Equals(right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別する文字列比較
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EqualsCaseSensitive(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length)
            return false;
        
        if (Vector.IsHardwareAccelerated && left.Length >= Vector<ushort>.Count)
        {
            return EqualsCaseSensitiveSimd(left, right);
        }
        
        // フォールバック
        return left.SequenceEqual(right);
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別しない文字列比較（内部実装）
    /// </summary>
    private static bool EqualsIgnoreCaseSimd(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        int vectorSize = Vector<ushort>.Count;
        int i = 0;
        
        // SIMDを使用してベクトル単位で処理
        for (; i <= left.Length - vectorSize; i += vectorSize)
        {
            var leftVector = CreateVectorFromSpan(left.Slice(i, vectorSize));
            var rightVector = CreateVectorFromSpan(right.Slice(i, vectorSize));
            
            // 各文字を小文字に変換してから比較
            var leftLower = ToLowerVector(leftVector);
            var rightLower = ToLowerVector(rightVector);
            
            if (!Vector.EqualsAll(Vector.Equals(leftLower, rightLower), Vector<ushort>.AllBitsSet))
            {
                return false;
            }
        }
        
        // 残りの要素を個別に処理
        for (; i < left.Length; i++)
        {
            if (char.ToLowerInvariant(left[i]) != char.ToLowerInvariant(right[i]))
            {
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// SIMD命令を使用した大文字小文字を区別する文字列比較（内部実装）
    /// </summary>
    private static bool EqualsCaseSensitiveSimd(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        int vectorSize = Vector<ushort>.Count;
        int i = 0;
        
        // SIMDを使用してベクトル単位で処理
        for (; i <= left.Length - vectorSize; i += vectorSize)
        {
            var leftVector = CreateVectorFromSpan(left.Slice(i, vectorSize));
            var rightVector = CreateVectorFromSpan(right.Slice(i, vectorSize));
            
            if (!Vector.EqualsAll(Vector.Equals(leftVector, rightVector), Vector<ushort>.AllBitsSet))
            {
                return false;
            }
        }
        
        // 残りの要素を個別に処理
        for (; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// ベクトル内の文字を小文字に変換する
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> ToLowerVector(Vector<ushort> vector)
    {
        // A-Z (65-90) を a-z (97-122) に変換
        var upperA = new Vector<ushort>(65);  // 'A'
        var upperZ = new Vector<ushort>(90);  // 'Z'
        var diff = new Vector<ushort>(32);    // 'a' - 'A'
        
        var isUpper = Vector.BitwiseAnd(
            Vector.GreaterThanOrEqual(vector, upperA),
            Vector.LessThanOrEqual(vector, upperZ)
        );
        
        var lowerVector = Vector.ConditionalSelect(isUpper, Vector.Add(vector, diff), vector);
        
        return lowerVector;
    }

    /// <summary>
    /// すべてのマッチした位置を返す
    /// </summary>
    /// <param name="source">検索対象の文字列</param>
    /// <param name="pattern">検索パターン</param>
    /// <param name="comparison">文字列比較方法</param>
    /// <returns>マッチした位置のリスト</returns>
    public static List<int> FindAllMatches(ReadOnlySpan<char> source, ReadOnlySpan<char> pattern, StringComparison comparison)
    {
        var matches = new List<int>();
        
        if (pattern.Length == 0)
            return matches;
        
        int startIndex = 0;
        
        while (startIndex <= source.Length - pattern.Length)
        {
            var remainingSource = source.Slice(startIndex);
            int matchIndex = IndexOf(remainingSource, pattern, comparison);
            
            if (matchIndex == -1)
                break;
            
            int absoluteIndex = startIndex + matchIndex;
            matches.Add(absoluteIndex);
            
            startIndex = absoluteIndex + 1;
        }
        
        return matches;
    }

    /// <summary>
    /// ReadOnlySpan&lt;char&gt;からVector&lt;ushort&gt;を作成するヘルパーメソッド
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> CreateVectorFromSpan(ReadOnlySpan<char> span)
    {
        // C#のcharはushortと同じサイズ（2バイト）
        var values = new ushort[Vector<ushort>.Count];
        var count = Math.Min(span.Length, values.Length);
        
        for (int i = 0; i < count; i++)
        {
            values[i] = span[i];
        }
        
        return new Vector<ushort>(values);
    }
}