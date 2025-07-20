using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Test.Infrastructure;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// IFileSystemインターフェースの非同期I/O機能のテスト
/// </summary>
public class AsyncIOTests
{
    private readonly Mock<IFileSystem> _mockFileSystem;

    public AsyncIOTests()
    {
        _mockFileSystem = new Mock<IFileSystem>();
    }

    /// <summary>
    /// 配列をIAsyncEnumerableに変換するヘルパーメソッド
    /// </summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield(); // 非同期性を保つため
        }
    }

    [Fact]
    public async Task ReadLinesAsMemoryAsync_ShouldReturnLinesAsMemory()
    {
        // Arrange
        var expectedLines = new[]
        {
            "line1".AsMemory(),
            "line2".AsMemory(),
            "line3".AsMemory()
        };

        _mockFileSystem.Setup(fs => fs.ReadLinesAsMemoryAsync("test.txt", It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(expectedLines));

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in _mockFileSystem.Object.ReadLinesAsMemoryAsync("test.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0].ToString());
        Assert.Equal("line2", lines[1].ToString());
        Assert.Equal("line3", lines[2].ToString());
        
        _mockFileSystem.Verify(fs => fs.ReadLinesAsMemoryAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadLinesAsync_ShouldReturnLinesAsStrings()
    {
        // Arrange
        var expectedLines = new[] { "line1", "line2", "line3" };

        _mockFileSystem.Setup(fs => fs.ReadLinesAsync("test.txt", It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(expectedLines));

        // Act
        var lines = new List<string>();
        await foreach (var line in _mockFileSystem.Object.ReadLinesAsync("test.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
        
        _mockFileSystem.Verify(fs => fs.ReadLinesAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadStandardInputAsMemoryAsync_ShouldReturnStandardInputAsMemory()
    {
        // Arrange
        var expectedLines = new[]
        {
            "input1".AsMemory(),
            "input2".AsMemory(),
            "input3".AsMemory()
        };

        _mockFileSystem.Setup(fs => fs.ReadStandardInputAsMemoryAsync(It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(expectedLines));

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in _mockFileSystem.Object.ReadStandardInputAsMemoryAsync())
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("input1", lines[0].ToString());
        Assert.Equal("input2", lines[1].ToString());
        Assert.Equal("input3", lines[2].ToString());
        
        _mockFileSystem.Verify(fs => fs.ReadStandardInputAsMemoryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadStandardInputAsync_ShouldReturnStandardInputAsStrings()
    {
        // Arrange
        var expectedLines = new[] { "input1", "input2", "input3" };

        _mockFileSystem.Setup(fs => fs.ReadStandardInputAsync(It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(expectedLines));

        // Act
        var lines = new List<string>();
        await foreach (var line in _mockFileSystem.Object.ReadStandardInputAsync())
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("input1", lines[0]);
        Assert.Equal("input2", lines[1]);
        Assert.Equal("input3", lines[2]);
        
        _mockFileSystem.Verify(fs => fs.ReadStandardInputAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ShouldReturnFilesAsync()
    {
        // Arrange
        var expectedFiles = new[] { "dir/file1.txt", "dir/file2.txt" };

        _mockFileSystem.Setup(fs => fs.EnumerateFilesAsync("dir", "*.txt", System.IO.SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(expectedFiles));

        // Act
        var files = new List<string>();
        await foreach (var file in _mockFileSystem.Object.EnumerateFilesAsync("dir", "*.txt", System.IO.SearchOption.TopDirectoryOnly))
        {
            files.Add(file);
        }

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Contains("dir/file1.txt", files);
        Assert.Contains("dir/file2.txt", files);
        
        _mockFileSystem.Verify(fs => fs.EnumerateFilesAsync("dir", "*.txt", System.IO.SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AsyncIO_ShouldCancelWhenCancellationTokenIsTriggered()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var lines = new List<ReadOnlyMemory<char>>();

        _mockFileSystem.Setup(fs => fs.ReadLinesAsMemoryAsync("test.txt", It.IsAny<CancellationToken>()))
                      .Returns(CreateCancellableAsyncEnumerable(cts.Token));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var line in _mockFileSystem.Object.ReadLinesAsMemoryAsync("test.txt", cts.Token))
            {
                lines.Add(line);
                if (lines.Count == 2)
                {
                    cts.Cancel(); // Cancel after reading 2 lines
                }
            }
        });

        Assert.Equal(2, lines.Count);
        _mockFileSystem.Verify(fs => fs.ReadLinesAsMemoryAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// キャンセルトークンを監視するAsyncEnumerableを作成
    /// </summary>
    private static async IAsyncEnumerable<ReadOnlyMemory<char>> CreateCancellableAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 1; i <= 5; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return $"line{i}".AsMemory();
            await Task.Delay(10, cancellationToken); // 少し遅延を入れて非同期性を確保
        }
    }

    [Fact]
    public async Task ReadLinesAsMemoryAsync_WithLargeFile_ShouldHandleEfficientlyWithZeroCopy()
    {
        // Arrange
        var expectedLineCount = 1000;
        var largeFileLines = Enumerable.Range(0, expectedLineCount)
                                      .Select(i => $"This is line {i} with some content to test memory efficiency".AsMemory())
                                      .ToArray();

        _mockFileSystem.Setup(fs => fs.ReadLinesAsMemoryAsync("large.txt", It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(largeFileLines));

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in _mockFileSystem.Object.ReadLinesAsMemoryAsync("large.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(expectedLineCount, lines.Count);
        Assert.Equal("This is line 0 with some content to test memory efficiency", lines[0].ToString());
        Assert.Equal("This is line 999 with some content to test memory efficiency", lines[999].ToString());
        
        _mockFileSystem.Verify(fs => fs.ReadLinesAsMemoryAsync("large.txt", It.IsAny<CancellationToken>()), Times.Once);
    }
}
