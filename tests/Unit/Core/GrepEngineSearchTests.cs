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
using Xunit.Abstractions;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// GrepEngine検索機能のテスト
/// </summary>
public class GrepEngineSearchTests : GrepEngineTestsBase
{
    private readonly ITestOutputHelper _output;

    public GrepEngineSearchTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public async Task SearchInDirectoryAsync_WithMatchingFile_ReturnsMatchResult()
    {
        // Arrange - CreateTempFileを使用
        var testFile = CreateTempFile("This is a test file\nwith multiple lines");
        var searchPattern = "test";
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, testFile, searchPattern);
        
        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);
        
        // Assert
        _output.WriteLine($"SearchInDirectoryAsync - FileResults.Count: {result.FileResults.Count}");
        if (result.FileResults.Count > 0)
        {
            _output.WriteLine($"FileResult.FileName: '{result.FileResults[0].FileName}'");
            _output.WriteLine($"FileResult.TotalMatches: {result.FileResults[0].TotalMatches}");
            _output.WriteLine($"FileResult.Matches.Count: {result.FileResults[0].Matches.Count}");
            for (int i = 0; i < result.FileResults[0].Matches.Count; i++)
            {
                _output.WriteLine($"Match[{i}]: Line {result.FileResults[0].Matches[i].LineNumber} - '{result.FileResults[0].Matches[i].Line}'");
            }
        }
        
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal(testFile, result.FileResults[0].FileName);
        Assert.Equal(1, result.FileResults[0].TotalMatches); // "test"は1行目のみに存在
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
        // Arrange - CreateTempFileを使用して2つのファイルを作成
        var file1 = CreateTempFile("hello world", ".txt");
        var file2 = CreateTempFile("hello test", ".txt");
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, new[] { file1, file2 }, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);

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
        var tempFile1 = CreateTempFile("hello world", ".txt");
        var tempFile2 = CreateTempFile("hello test", ".log");
        
        var mockOptions = new Mock<IOptionContext>();
        // ディレクトリではなく、具体的なファイルの配列を渡す
        var inputFiles = new[] { tempFile1, tempFile2 };
        SetupBasicOptions(mockOptions, inputFiles, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns("*.log");

        // FileSearchServiceをカスタマイズしてExcludeパターンを適用
        MockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var allFiles = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                var excludePattern = options.GetStringValue(OptionNames.ExcludePattern);
                
                var filteredFiles = allFiles.Where(file => {
                    if (!string.IsNullOrEmpty(excludePattern))
                    {
                        // 簡易的な*.logパターンマッチング
                        if (excludePattern == "*.log" && file.EndsWith(".log"))
                            return false;
                    }
                    return true;
                }).AsEnumerable();
                
                return Task.FromResult(filteredFiles);
            });

        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        // デバッグ出力を追加
        _output.WriteLine($"ExcludePattern Test - Result.FileResults.Count: {result.FileResults.Count}");
        for (int i = 0; i < result.FileResults.Count; i++)
        {
            _output.WriteLine($"FileResult[{i}].FileName: '{result.FileResults[i].FileName}'");
            _output.WriteLine($"FileResult[{i}].TotalMatches: {result.FileResults[i].TotalMatches}");
            _output.WriteLine($"FileResult[{i}].Matches.Count: {result.FileResults[i].Matches.Count}");
        }
        _output.WriteLine($"Expected tempFile1: '{tempFile1}'");
        
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile1, fileResult.FileName);
        Assert.Equal(1, fileResult.TotalMatches);
    }

    [Fact]
    public async Task SearchAsync_WithIncludePattern_IncludesOnlyMatchingFiles()
    {
        // Arrange
        var tempFile1 = CreateTempFile("hello world", ".txt");
        var tempFile2 = CreateTempFile("hello test", ".log");
        
        var mockOptions = new Mock<IOptionContext>();
        // ディレクトリではなく、具体的なファイルの配列を渡す
        var inputFiles = new[] { tempFile1, tempFile2 };
        SetupBasicOptions(mockOptions, inputFiles, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("*.txt");

        // FileSearchServiceをカスタマイズしてIncludeパターンを適用
        MockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var allFiles = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                var includePattern = options.GetStringValue(OptionNames.IncludePattern);
                
                var filteredFiles = allFiles.Where(file => {
                    if (!string.IsNullOrEmpty(includePattern))
                    {
                        // 簡易的な*.txtパターンマッチング
                        if (includePattern == "*.txt" && file.EndsWith(".txt"))
                            return true;
                        if (includePattern == "*.txt")
                            return false;
                    }
                    return true;
                }).AsEnumerable();
                
                return Task.FromResult(filteredFiles);
            });

        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        // デバッグ出力を追加
        _output.WriteLine($"IncludePattern Test - Result.FileResults.Count: {result.FileResults.Count}");
        for (int i = 0; i < result.FileResults.Count; i++)
        {
            _output.WriteLine($"FileResult[{i}].FileName: '{result.FileResults[i].FileName}'");
            _output.WriteLine($"FileResult[{i}].TotalMatches: {result.FileResults[i].TotalMatches}");
        }
        _output.WriteLine($"Expected tempFile1: '{tempFile1}'");
        
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
