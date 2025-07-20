using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Constants;
using GrepCompatible.CommandLine;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GrepCompatible.Test.Integration;

/// <summary>
/// MockFileSystemを使用したクロスプラットフォーム対応テスト例
/// </summary>
public class MockFileSystemIntegrationTests
{
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly Mock<IFileSystem> _mockFileSystem = new();
    private readonly Mock<IPath> _mockPathHelper = new();
    private readonly Mock<IFileSearchService> _mockFileSearchService = new();
    private readonly Mock<IPerformanceOptimizer> _mockPerformanceOptimizer = new();
    private readonly Mock<IMatchResultPool> _mockMatchResultPool = new();
    private readonly ParallelGrepEngine _engine;

    public MockFileSystemIntegrationTests()
    {
        SetupPathHelper();
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            _mockFileSystem.Object,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _mockMatchResultPool.Object);
    }

    private void SetupPathHelper()
    {
        _mockPathHelper.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetDirectoryName(path));
        _mockPathHelper.Setup(p => p.GetFileName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFileName(path));
        _mockPathHelper.Setup(p => p.Combine(It.IsAny<string[]>()))
            .Returns<string[]>(paths => Path.Combine(paths));
    }

    [Fact]
    public async Task SearchAsync_WithMockFileSystem_ShouldWorkAcrossAllPlatforms()
    {
        // Arrange - FileSystemTestBuilderを使用したインターフェース経由の設定
        var testFile = "src/test.txt";
        var testContent = "This is a test file\nwith multiple lines";
        
        // FileSystemTestBuilderを使用してファイルシステムモックを構築
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile(testFile, testContent)
            .WithFiles(testFile)
            .Build();
        
        // エンジンを再構築（新しいファイルシステムで）
        var engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            fileSystem,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _mockMatchResultPool.Object);
        
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
    public async Task FileSystemMock_PathHandling_ShouldSupportDifferentFormats()
    {
        // Arrange - インターフェース経由でのパス処理テスト
        var windowsStylePath = "src\\test.txt";
        var unixStylePath = "src/test.txt"; 
        var content = "test content line 1\ntest content line 2";
        
        // FileSystemTestBuilderを使って両方のパス形式をサポート
        var fileSystem = new FileSystemTestBuilder()
            .WithFile(windowsStylePath, content)
            .WithFile(unixStylePath, content)
            .Build();
        
        // Act & Assert - 両方の形式で読み取り可能
        var lines1 = new List<string>();
        await foreach (var line in fileSystem.ReadLinesAsync(windowsStylePath))
        {
            lines1.Add(line);
        }
        
        var lines2 = new List<string>();
        await foreach (var line in fileSystem.ReadLinesAsync(unixStylePath))
        {
            lines2.Add(line);
        }
        
        Assert.Equal(2, lines1.Count);
        Assert.Equal(2, lines2.Count);
        Assert.Equal("test content line 1", lines1[0]);
        Assert.Equal("test content line 1", lines2[0]);
    }

    [Fact]
    public async Task FileSystemMock_DirectoryListing_ShouldEnumerateFiles()
    {
        // Arrange - FileSystemTestBuilderを使用したディレクトリ・ファイル構造の構築
        var fileSystem = new FileSystemTestBuilder()
            .WithDirectory("src")
            .WithFile("src/file1.txt", "content1")
            .WithFile("src/file2.txt", "content2")
            .WithFile("src/subdir/file3.txt", "content3")
            .WithFiles("src/file1.txt", "src/file2.txt", "src/subdir/file3.txt")
            .Build();
        
        // Act - 非同期ファイル列挙のテスト
        var allFiles = new List<string>();
        await foreach (var file in fileSystem.EnumerateFilesAsync("src", "*", System.IO.SearchOption.AllDirectories))
        {
            allFiles.Add(file);
        }
        
        // Assert
        Assert.Equal(3, allFiles.Count);
        Assert.Contains("src/file1.txt", allFiles);
        Assert.Contains("src/file2.txt", allFiles);
        Assert.Contains("src/subdir/file3.txt", allFiles);
    }

    [Fact]
    public async Task SearchAsync_WithStandardInput_ShouldWorkCorrectly()
    {
        // Arrange - FileSystemTestBuilderを使用した標準入力設定
        var standardInputContent = "line1 with test\nline2 without match\nline3 with test again";
        var fileSystem = new FileSystemTestBuilder()
            .WithStandardInput(standardInputContent)
            .Build();
        
        // エンジンを標準入力対応で再構築
        var engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            fileSystem,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _mockMatchResultPool.Object);
        
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
