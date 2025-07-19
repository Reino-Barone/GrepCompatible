using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Test.Infrastructure;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// 非同期I/Oの最適化機能のテスト
/// </summary>
public class AsyncIOTests
{
    [Fact]
    public async Task ReadLinesAsMemoryAsync_ShouldReturnLinesAsMemory()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var testContent = "line1\nline2\nline3";
        mockFileSystem.AddFile("test.txt", testContent);

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in mockFileSystem.ReadLinesAsMemoryAsync("test.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0].ToString());
        Assert.Equal("line2", lines[1].ToString());
        Assert.Equal("line3", lines[2].ToString());
    }

    [Fact]
    public async Task ReadLinesAsync_ShouldReturnLinesAsStrings()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var testContent = "line1\nline2\nline3";
        mockFileSystem.AddFile("test.txt", testContent);

        // Act
        var lines = new List<string>();
        await foreach (var line in mockFileSystem.ReadLinesAsync("test.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public async Task ReadStandardInputAsMemoryAsync_ShouldReturnStandardInputAsMemory()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.SetStandardInput("input1\ninput2\ninput3");

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in mockFileSystem.ReadStandardInputAsMemoryAsync())
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("input1", lines[0].ToString());
        Assert.Equal("input2", lines[1].ToString());
        Assert.Equal("input3", lines[2].ToString());
    }

    [Fact]
    public async Task ReadStandardInputAsync_ShouldReturnStandardInputAsStrings()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.SetStandardInput("input1\ninput2\ninput3");

        // Act
        var lines = new List<string>();
        await foreach (var line in mockFileSystem.ReadStandardInputAsync())
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("input1", lines[0]);
        Assert.Equal("input2", lines[1]);
        Assert.Equal("input3", lines[2]);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ShouldReturnFilesAsync()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile("dir/file1.txt", "content1");
        mockFileSystem.AddFile("dir/file2.txt", "content2");
        mockFileSystem.AddFile("dir/file3.log", "content3");

        // Act
        var files = new List<string>();
        await foreach (var file in mockFileSystem.EnumerateFilesAsync("dir", "*.txt", System.IO.SearchOption.TopDirectoryOnly))
        {
            files.Add(file);
        }

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Contains("dir/file1.txt", files);
        Assert.Contains("dir/file2.txt", files);
        Assert.DoesNotContain("dir/file3.log", files);
    }

    [Fact]
    public async Task AsyncIO_ShouldCancelWhenCancellationTokenIsTriggered()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var testContent = "line1\nline2\nline3\nline4\nline5";
        mockFileSystem.AddFile("test.txt", testContent);

        using var cts = new CancellationTokenSource();
        var lines = new List<ReadOnlyMemory<char>>();
        int lineCount = 0;

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var line in mockFileSystem.ReadLinesAsMemoryAsync("test.txt", cts.Token))
            {
                lines.Add(line);
                lineCount++;
                if (lineCount == 2)
                {
                    cts.Cancel(); // Cancel after reading 2 lines
                }
            }
        });

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task ReadLinesAsMemoryAsync_WithLargeFile_ShouldHandleEfficientlyWithZeroCopy()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var sb = new StringBuilder();
        var expectedLineCount = 1000;
        
        for (int i = 0; i < expectedLineCount; i++)
        {
            sb.AppendLine($"This is line {i} with some content to test memory efficiency");
        }
        
        mockFileSystem.AddFile("large.txt", sb.ToString());

        // Act
        var lines = new List<ReadOnlyMemory<char>>();
        await foreach (var line in mockFileSystem.ReadLinesAsMemoryAsync("large.txt"))
        {
            lines.Add(line);
        }

        // Assert
        Assert.Equal(expectedLineCount, lines.Count);
        Assert.Equal("This is line 0 with some content to test memory efficiency", lines[0].ToString());
        Assert.Equal("This is line 999 with some content to test memory efficiency", lines[999].ToString());
    }
}
