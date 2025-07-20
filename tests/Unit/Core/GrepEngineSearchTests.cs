using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.CommandLine;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// GrepEngine検索機能のテスト
/// </summary>
public class GrepEngineSearchTests : GrepEngineTestsBase
{
    [Fact]
    public async Task SearchInDirectoryAsync_WithMatchingFile_ReturnsMatchResult()
    {
        // Arrange
        var testFile = "testdir/test.txt";
        var testContent = "This is a test file\nwith multiple lines";
        var searchPattern = "test";
        
        // Mock file system to return file content
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(testFile, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(testContent.Split('\n')));
        
        MockFileSystem.Setup(fs => fs.FileExists(testFile))
                      .Returns(true);
        
        var matches = new List<MatchResult>
        {
            new(testFile, 1, "test", "This is a test file".AsMemory(), 10, 4)
        };
        
        MockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                if (line.Contains("test"))
                {
                    return new List<MatchResult> { new(fileName, lineNumber, line, "test".AsMemory(), line.IndexOf("test"), 4) };
                }
                return Enumerable.Empty<MatchResult>();
            });
        
        var options = new DynamicOptions();
        // ファイルリストとパターンを設定
        options.AddArgument(new StringArgument(ArgumentNames.Pattern, searchPattern));
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse(testFile);
        options.AddArgument(filesArg);
        
        // Act
        var result = await Engine.SearchAsync(options);
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal(testFile, result.FileResults[0].FileName);
        Assert.Equal(1, result.FileResults[0].TotalMatches);
        Assert.True(result.FileResults[0].HasMatches);
    }

    [Fact]
    public async Task SearchAsync_WithSingleFile_ReturnsCorrectResult()
    {
        // Arrange
        var tempFile = CreateTempFile("hello world\ntest line\nhello again");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        
        var expectedMatches = new List<MatchResult>
        {
            new(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5),
            new(tempFile, 3, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { expectedMatches[0] });
        MockStrategy.Setup(s => s.FindMatches("test line", "hello", mockOptions.Object, tempFile, 2))
            .Returns(Array.Empty<MatchResult>());
        MockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { expectedMatches[1] });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile, fileResult.FileName);
        Assert.Equal(2, fileResult.TotalMatches);
        Assert.True(fileResult.HasMatches);
        Assert.False(fileResult.HasError);
        Assert.Equal(2, fileResult.Matches.Count);
        
        // マッチ結果の検証（順序に依存しない）
        Assert.Contains(fileResult.Matches, m => m.LineNumber == 1 && m.Line == "hello world");
        Assert.Contains(fileResult.Matches, m => m.LineNumber == 3 && m.Line == "hello again");
    }

    [Fact]
    public async Task SearchAsync_WithMultipleFiles_ReturnsCorrectResults()
    {
        // Arrange
        var tempFile1 = CreateTempFile("hello world");
        var tempFile2 = CreateTempFile("test hello");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, new[] { tempFile1, tempFile2 }, "hello");
        
        var expectedMatch1 = new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5);
        var expectedMatch2 = new MatchResult(tempFile2, 1, "test hello", "hello".AsMemory(), 5, 10);
        
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { expectedMatch1 });
        MockStrategy.Setup(s => s.FindMatches("test hello", "hello", mockOptions.Object, tempFile2, 1))
            .Returns(new[] { expectedMatch2 });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches);
        Assert.Equal(2, result.TotalFiles);
        
        // ファイル結果の検証（順序に依存しない）
        Assert.Contains(result.FileResults, fr => fr.FileName == tempFile1 && fr.TotalMatches == 1);
        Assert.Contains(result.FileResults, fr => fr.FileName == tempFile2 && fr.TotalMatches == 1);
    }

    [Fact]
    public async Task SearchAsync_WithInvertMatch_ReturnsCorrectResults()
    {
        // Arrange
        var tempFile = CreateTempFile("hello world\ntest line\nhello again");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(true);
        
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        MockStrategy.Setup(s => s.FindMatches("test line", "hello", mockOptions.Object, tempFile, 2))
            .Returns(Array.Empty<MatchResult>());
        MockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { new MatchResult(tempFile, 3, "hello again", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(1, fileResult.TotalMatches);
        Assert.Single(fileResult.Matches);
        Assert.Equal(2, fileResult.Matches[0].LineNumber);
        Assert.Equal("test line", fileResult.Matches[0].Line);
    }

    [Fact]
    public async Task SearchAsync_WithMaxCount_LimitsResults()
    {
        // Arrange
        var tempFile = CreateTempFile("hello world\nhello test\nhello again");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns(2);
        
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        MockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, tempFile, 2))
            .Returns(new[] { new MatchResult(tempFile, 2, "hello test", "hello".AsMemory(), 0, 5) });
        MockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { new MatchResult(tempFile, 3, "hello again", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(2, fileResult.TotalMatches);
        Assert.Equal(2, fileResult.Matches.Count);
        
        // 最初の2つのマッチのみが含まれることを確認
        Assert.All(fileResult.Matches, match => Assert.True(match.LineNumber <= 2));
    }

    [Fact]
    public async Task SearchAsync_WithRecursiveSearch_FindsFilesInSubdirectories()
    {
        // Arrange
        var tempDir = "testdir";
        var subDir = tempDir + "/subdir";
        
        var file1 = tempDir + "/file1.txt";
        var file2 = subDir + "/file2.txt";
        
        // Mock file system to return file contents
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(file1, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello world" }));
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(file2, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello test" }));
        
        MockFileSystem.Setup(fs => fs.FileExists(file1)).Returns(true);
        MockFileSystem.Setup(fs => fs.FileExists(file2)).Returns(true);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello world", "hello".AsMemory(), 0, 5) });
        MockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello test", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches);
        
        // ファイル結果の検証（順序に依存しない）
        Assert.Contains(result.FileResults, fr => fr.FileName == file1 && fr.TotalMatches == 1);
        Assert.Contains(result.FileResults, fr => fr.FileName == file2 && fr.TotalMatches == 1);
    }

    [Fact]
    public async Task SearchAsync_WithExcludePattern_ExcludesMatchingFiles()
    {
        // Arrange
        var tempDir = "testdir";
        var tempFile1 = tempDir + "/test.txt";
        var tempFile2 = tempDir + "/test.log";
        
        // Mock file system to return file contents
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(tempFile1, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello world" }));
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(tempFile2, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello test" }));
        
        MockFileSystem.Setup(fs => fs.FileExists(tempFile1)).Returns(true);
        MockFileSystem.Setup(fs => fs.FileExists(tempFile2)).Returns(true);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns("*.log");

        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile1, fileResult.FileName);
        Assert.Equal(1, fileResult.TotalMatches);
    }

    [Fact]
    public async Task SearchAsync_WithIncludePattern_IncludesOnlyMatchingFiles()
    {
        // Arrange
        var tempDir = "testdir";
        var tempFile1 = tempDir + "/test.txt";
        var tempFile2 = tempDir + "/test.log";
        
        // Mock file system to return file contents
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(tempFile1, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello world" }));
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(tempFile2, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(new[] { "hello test" }));
        
        MockFileSystem.Setup(fs => fs.FileExists(tempFile1)).Returns(true);
        MockFileSystem.Setup(fs => fs.FileExists(tempFile2)).Returns(true);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("*.txt");

        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile1, fileResult.FileName);
        Assert.Equal(1, fileResult.TotalMatches);
    }

    [Fact]
    public async Task SearchAsync_WithLargeFile_ProcessesEfficiently()
    {
        // Arrange
        var largeContent = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            largeContent.AppendLine($"line {i} with hello content");
        }
        
        var tempFile = CreateTempFile(largeContent.ToString());
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        
        // 各行でマッチを返すように設定
        MockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), "hello", mockOptions.Object, tempFile, It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                if (line.Contains("hello"))
                {
                    var startIndex = line.IndexOf("hello", StringComparison.Ordinal);
                    return new[] { new MatchResult(fileName, lineNumber, line, "hello".AsMemory(), startIndex, startIndex + 5) };
                }
                return Array.Empty<MatchResult>();
            });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(1000, fileResult.TotalMatches);
        Assert.True(fileResult.HasMatches);
        Assert.False(fileResult.HasError);
        Assert.True(result.ElapsedTime > TimeSpan.Zero);
    }
}
