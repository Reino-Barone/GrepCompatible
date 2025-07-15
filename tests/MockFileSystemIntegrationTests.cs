using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Constants;
using GrepCompatible.CommandLine;
using Moq;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// MockFileSystemを使用したクロスプラットフォーム対応テスト例
/// </summary>
public class MockFileSystemIntegrationTests
{
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly MockPathHelper _mockPathHelper = new();
    private readonly ParallelGrepEngine _engine;

    public MockFileSystemIntegrationTests()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(_mockStrategyFactory.Object, _mockFileSystem, _mockPathHelper);
    }

    [Fact]
    public async Task SearchAsync_WithMockFileSystem_ShouldWorkAcrossAllPlatforms()
    {
        // Arrange - 実行環境に依存しないテスト用パス
        var testFile = "src/test.txt";
        var testContent = "This is a test file\nwith multiple lines";
        
        _mockFileSystem.AddFile(testFile, testContent);
        
        var matches = new List<MatchResult>
        {
            new(testFile, 1, "This is a test file", "test".AsMemory(), 10, 4)
        };
        
        _mockStrategy.Setup(s => s.FindMatches(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<IOptionContext>(), 
            It.IsAny<string>(), 
            It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                // ファイル名に応じて適切なマッチを返す
                if (fileName.Contains("test.txt") && line.Contains("test"))
                {
                    return new List<MatchResult> { new(fileName, lineNumber, line, "test".AsMemory(), line.IndexOf("test"), 4) };
                }
                return Enumerable.Empty<MatchResult>();
            });
        
        var options = new DynamicOptions();
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse(testFile);
        options.AddArgument(filesArg);
        options.AddArgument(new StringArgument(ArgumentNames.Pattern, "test"));
        
        // Act
        var result = await _engine.SearchAsync(options);
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal(testFile, result.FileResults[0].FileName);
        Assert.Single(result.FileResults[0].Matches);
        Assert.Equal("test", result.FileResults[0].Matches[0].MatchedText.ToString());
    }

    [Fact]
    public void MockFileSystem_PathHandling_ShouldBeEnvironmentIndependent()
    {
        // Arrange - 異なるパス形式を使用してテスト
        var windowsStylePath = "src\\test.txt";
        var unixStylePath = "src/test.txt";
        var content = "test content";
        
        // Act - 両方の形式で同じファイルを参照
        _mockFileSystem.AddFile(windowsStylePath, content);
        
        // Assert - 両方の形式でアクセス可能
        Assert.True(_mockFileSystem.FileExists(windowsStylePath));
        Assert.True(_mockFileSystem.FileExists(unixStylePath));
        
        var info1 = _mockFileSystem.GetFileInfo(windowsStylePath);
        var info2 = _mockFileSystem.GetFileInfo(unixStylePath);
        
        Assert.Equal(info1.Length, info2.Length);
        Assert.Equal(info1.Name, info2.Name);
    }

    [Fact]
    public void MockFileSystem_DirectoryListing_ShouldBeConsistent()
    {
        // Arrange
        _mockFileSystem.AddDirectory("src");
        _mockFileSystem.AddFile("src/file1.txt", "content1");
        _mockFileSystem.AddFile("src/file2.txt", "content2");
        _mockFileSystem.AddFile("src/subdir/file3.txt", "content3");
        
        // Act
        var topLevelFiles = _mockFileSystem.EnumerateFiles("src", "*", SearchOption.TopDirectoryOnly);
        var allFiles = _mockFileSystem.EnumerateFiles("src", "*", SearchOption.AllDirectories);
        
        // Assert
        Assert.Equal(2, topLevelFiles.Count());
        Assert.Equal(3, allFiles.Count());
        
        Assert.Contains("src/file1.txt", topLevelFiles);
        Assert.Contains("src/file2.txt", topLevelFiles);
        Assert.Contains("src/subdir/file3.txt", allFiles);
    }

    [Fact]
    public async Task SearchAsync_WithStandardInput_ShouldWorkCorrectly()
    {
        // Arrange - 標準入力の内容を設定
        var standardInputContent = "line1 with test\nline2 without match\nline3 with test again";
        _mockFileSystem.SetStandardInput(standardInputContent);
        
        var matches = new List<MatchResult>
        {
            new("(standard input)", 1, "line1 with test", "test".AsMemory(), 11, 4),
            new("(standard input)", 3, "line3 with test again", "test".AsMemory(), 11, 4)
        };
        
        _mockStrategy.Setup(s => s.FindMatches(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<IOptionContext>(), 
            It.IsAny<string>(), 
            It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                if (line.Contains("test"))
                {
                    return new List<MatchResult> { matches.FirstOrDefault(m => m.LineNumber == lineNumber)! }.Where(m => m != null);
                }
                return Enumerable.Empty<MatchResult>();
            });
        
        var options = new DynamicOptions();
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse("-"); // 標準入力を指定
        options.AddArgument(filesArg);
        options.AddArgument(new StringArgument(ArgumentNames.Pattern, "test"));
        
        // Act
        var result = await _engine.SearchAsync(options);
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal("(standard input)", result.FileResults[0].FileName);
        Assert.Equal(2, result.FileResults[0].TotalMatches);
    }
}
