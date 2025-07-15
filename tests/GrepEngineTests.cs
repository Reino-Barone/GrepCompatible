using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
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
    private readonly ParallelGrepEngine _engine;

    public GrepEngineTests()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(_mockStrategyFactory.Object);
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
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, nonExistentFile, "hello");

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Empty(result.FileResults);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal(0, result.TotalFiles);
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
        var fileInfo = new FileInfo(tempFile);
        fileInfo.IsReadOnly = true;
        
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
        
        // Cleanup
        fileInfo.IsReadOnly = false;
    }

    [Fact]
    public async Task SearchAsync_WithRecursiveSearch_FindsFilesInSubdirectories()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var subDir = Path.Combine(tempDir, "subdir");
        Directory.CreateDirectory(subDir);
        
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(subDir, "file2.txt");
        File.WriteAllText(file1, "hello world");
        File.WriteAllText(file2, "hello test");
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
        var tempFile1 = Path.Combine(tempDir, "test.txt");
        var tempFile2 = Path.Combine(tempDir, "test.log");
        File.WriteAllText(tempFile1, "hello world");
        File.WriteAllText(tempFile2, "hello test");
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
        var tempFile1 = Path.Combine(tempDir, "test.txt");
        var tempFile2 = Path.Combine(tempDir, "test.log");
        File.WriteAllText(tempFile1, "hello world");
        File.WriteAllText(tempFile2, "hello test");
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
        Assert.Throws<ArgumentNullException>(() => new ParallelGrepEngine(null!));
    }

    private string CreateTempFile(string content, string extension = ".txt")
    {
        var tempFile = Path.GetTempFileName();
        if (extension != ".tmp")
        {
            var newTempFile = Path.ChangeExtension(tempFile, extension);
            File.Move(tempFile, newTempFile);
            tempFile = newTempFile;
        }
        
        File.WriteAllText(tempFile, content);
        _tempFiles.Add(tempFile);
        return tempFile;
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
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
        foreach (var tempFile in _tempFiles)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                else if (Directory.Exists(tempFile))
                {
                    Directory.Delete(tempFile, true);
                }
            }
            catch
            {
                // テンポラリファイルの削除に失敗してもテストは続行
            }
        }
        _tempFiles.Clear();
    }
}
