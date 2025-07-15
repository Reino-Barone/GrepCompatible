using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.CommandLine;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using GrepCompatible.Test.Infrastructure;
using Moq;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace GrepCompatible.Test;

public class GrepEngineTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly MockPathHelper _mockPathHelper = new();
    private readonly ParallelGrepEngine _engine;

    public GrepEngineTests()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(_mockStrategyFactory.Object, _mockFileSystem, _mockPathHelper);
    }

    [Fact]
    public async Task SearchInDirectoryAsync_WithMatchingFile_ReturnsMatchResult()
    {
        // Arrange
        var testDir = "testdir";
        var testFile = "testdir/test.txt";
        var testContent = "This is a test file\nwith multiple lines";
        var searchPattern = "test";
        
        _mockFileSystem.AddDirectory(testDir);
        _mockFileSystem.AddFile(testFile, testContent);
        
        var matches = new List<MatchResult>
        {
            new(testFile, 1, "test", "This is a test file".AsMemory(), 10, 4)
        };
        
        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
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
        var result = await _engine.SearchAsync(options);
        
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
        
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { expectedMatches[0] });
        _mockStrategy.Setup(s => s.FindMatches("test line", "hello", mockOptions.Object, tempFile, 2))
            .Returns(Array.Empty<MatchResult>());
        _mockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { expectedMatches[1] });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { expectedMatch1 });
        _mockStrategy.Setup(s => s.FindMatches("test hello", "hello", mockOptions.Object, tempFile2, 1))
            .Returns(new[] { expectedMatch2 });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("test line", "hello", mockOptions.Object, tempFile, 2))
            .Returns(Array.Empty<MatchResult>());
        _mockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { new MatchResult(tempFile, 3, "hello again", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, tempFile, 2))
            .Returns(new[] { new MatchResult(tempFile, 2, "hello test", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, tempFile, 3))
            .Returns(new[] { new MatchResult(tempFile, 3, "hello again", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(2, fileResult.TotalMatches);
        Assert.Equal(2, fileResult.Matches.Count);
        
        // 最初の2つのマッチのみが含まれることを確認
        Assert.All(fileResult.Matches, match => Assert.True(match.LineNumber <= 2));
    }

    [Fact]
    public async Task SearchAsync_WithCancellation_ReturnsCancelledResult()
    {
        // Arrange
        var tempFile = CreateTempFile("hello world");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object, cts.Token);

        // Assert
        Assert.Empty(result.FileResults);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal(0, result.TotalFiles);
    }

    [Fact]
    public async Task SearchAsync_WithNonExistentFile_ReturnsEmptyResult()
    {
        // Arrange
        var nonExistentFile = $"non_existent_{Guid.NewGuid()}.txt";
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, nonExistentFile, "hello");

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        // 非存在ファイルの場合、グロブ展開が失敗してファイルが見つからない、または
        // エラーを含む結果が返されるはず
        if (result.FileResults.Count > 0)
        {
            // エラーが発生した場合
            Assert.Single(result.FileResults);
            Assert.True(result.FileResults[0].HasError);
            Assert.Equal(0, result.FileResults[0].TotalMatches);
        }
        else
        {
            // ファイルが見つからなかった場合
            Assert.Empty(result.FileResults);
            Assert.Equal(0, result.TotalMatches);
            Assert.Equal(0, result.TotalFiles);
        }
    }

    [Fact]
    public async Task SearchAsync_WithEmptyFile_ReturnsEmptyResult()
    {
        // Arrange
        var tempFile = CreateTempFile("");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(0, fileResult.TotalMatches);
        Assert.False(fileResult.HasMatches);
        Assert.Empty(fileResult.Matches);
    }

    [Fact]
    public async Task SearchAsync_WithReadOnlyFile_ProcessesSuccessfully()
    {
        // Arrange
        var tempFile = CreateTempFile("hello world");
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        
        var expectedMatch = new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5);
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { expectedMatch });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile, fileResult.FileName);
        Assert.Equal(1, fileResult.TotalMatches);
        Assert.False(fileResult.HasError);
    }

    [Fact]
    public async Task SearchAsync_WithRecursiveSearch_FindsFilesInSubdirectories()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var subDir = tempDir + "/subdir";
        _mockFileSystem.AddDirectory(subDir);
        
        var file1 = tempDir + "/file1.txt";
        var file2 = subDir + "/file2.txt";
        _mockFileSystem.AddFile(file1, "hello world");
        _mockFileSystem.AddFile(file2, "hello test");
        _tempFiles.Add(file1);
        _tempFiles.Add(file2);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello test", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        var tempDir = CreateTempDirectory();
        var tempFile1 = tempDir + "/test.txt";
        var tempFile2 = tempDir + "/test.log";
        _mockFileSystem.AddFile(tempFile1, "hello world");
        _mockFileSystem.AddFile(tempFile2, "hello test");
        _tempFiles.Add(tempFile1);
        _tempFiles.Add(tempFile2);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns("test.log");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        var tempDir = CreateTempDirectory();
        var tempFile1 = tempDir + "/test.txt";
        var tempFile2 = tempDir + "/test.log";
        _mockFileSystem.AddFile(tempFile1, "hello world");
        _mockFileSystem.AddFile(tempFile2, "hello test");
        _tempFiles.Add(tempFile1);
        _tempFiles.Add(tempFile2);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("test.txt");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile1, 1))
            .Returns(new[] { new MatchResult(tempFile1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

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
        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), "hello", mockOptions.Object, tempFile, It.IsAny<int>()))
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
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(1000, fileResult.TotalMatches);
        Assert.True(fileResult.HasMatches);
        Assert.False(fileResult.HasError);
        Assert.True(result.ElapsedTime > TimeSpan.Zero);
    }

    [Fact]
    public void Constructor_WithNullStrategyFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelGrepEngine(null!, _mockFileSystem, _mockPathHelper));
    }

    [Fact]
    public void Constructor_WithNullFileSystem_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelGrepEngine(_mockStrategyFactory.Object, null!, _mockPathHelper));
    }

    [Fact]
    public void Constructor_WithNullPathHelper_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelGrepEngine(_mockStrategyFactory.Object, _mockFileSystem, null!));
    }

    private string CreateTempFile(string content, string extension = ".txt")
    {
        var tempFile = $"temp_{Guid.NewGuid()}{extension}";
        
        // モックファイルシステムにファイルを追加
        _mockFileSystem.AddFile(tempFile, content);
        _tempFiles.Add(tempFile);
        return tempFile;
    }

    private string CreateTempDirectory()
    {
        var tempDir = $"temp_dir_{Guid.NewGuid()}";
        _mockFileSystem.AddDirectory(tempDir);
        _tempFiles.Add(tempDir);
        return tempDir;
    }

    private static void SetupBasicOptions(Mock<IOptionContext> mockOptions, string file, string pattern)
    {
        SetupBasicOptions(mockOptions, new[] { file }, pattern);
    }

    private static void SetupBasicOptions(Mock<IOptionContext> mockOptions, string[] files, string pattern)
    {
        mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(pattern);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(files.ToList().AsReadOnly());
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(false);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns((string?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns((string?)null);
    }

    public void Dispose()
    {
        // モックファイルシステムをクリア
        _mockFileSystem.Clear();
        _tempFiles.Clear();
    }
}
