using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.CommandLine;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using GrepCompatible.Test.Infrastructure;
using Moq;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// グロブパターンの--include/--exclude機能のテスト
/// </summary>
public class GlobPatternTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly MockPathHelper _mockPathHelper = new();
    private readonly ParallelGrepEngine _engine;

    public GlobPatternTests()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(_mockStrategyFactory.Object, _mockFileSystem, _mockPathHelper);
    }

    [Fact]
    public async Task SearchAsync_WithExcludeGlobPattern_ExcludesMatchingFiles()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var csharpFile = tempDir + "/Program.cs";
        var textFile = tempDir + "/README.txt";
        var logFile = tempDir + "/debug.log";
        
        _mockFileSystem.AddFile(csharpFile, "hello world");
        _mockFileSystem.AddFile(textFile, "hello test");
        _mockFileSystem.AddFile(logFile, "hello debug");
        _tempFiles.Add(csharpFile);
        _tempFiles.Add(textFile);
        _tempFiles.Add(logFile);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns("*.log");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, csharpFile, 1))
            .Returns(new[] { new MatchResult(csharpFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, textFile, 1))
            .Returns(new[] { new MatchResult(textFile, 1, "hello test", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches);
        
        // .logファイルが除外されていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == csharpFile);
        Assert.Contains(result.FileResults, fr => fr.FileName == textFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == logFile);
    }

    [Fact]
    public async Task SearchAsync_WithIncludeGlobPattern_IncludesOnlyMatchingFiles()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var csharpFile = tempDir + "/Program.cs";
        var textFile = tempDir + "/README.txt";
        var logFile = tempDir + "/debug.log";
        
        _mockFileSystem.AddFile(csharpFile, "hello world");
        _mockFileSystem.AddFile(textFile, "hello test");
        _mockFileSystem.AddFile(logFile, "hello debug");
        _tempFiles.Add(csharpFile);
        _tempFiles.Add(textFile);
        _tempFiles.Add(logFile);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("*.cs");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, csharpFile, 1))
            .Returns(new[] { new MatchResult(csharpFile, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        Assert.Equal(1, result.TotalMatches);
        
        // .csファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == csharpFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == textFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == logFile);
    }

    [Fact]
    public async Task SearchAsync_WithQuestionMarkGlobPattern_MatchesSingleCharacter()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var file1 = tempDir + "/test1.txt";
        var file2 = tempDir + "/test2.txt";
        var file3 = tempDir + "/test10.txt";
        var file4 = tempDir + "/test.txt";
        
        _mockFileSystem.AddFile(file1, "hello world");
        _mockFileSystem.AddFile(file2, "hello test");
        _mockFileSystem.AddFile(file3, "hello debug");
        _mockFileSystem.AddFile(file4, "hello sample");
        _tempFiles.Add(file1);
        _tempFiles.Add(file2);
        _tempFiles.Add(file3);
        _tempFiles.Add(file4);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("test?.txt");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello test", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches);
        
        // 1文字のファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.Contains(result.FileResults, fr => fr.FileName == file2);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file3); // test10.txt は除外
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4); // test.txt は除外
    }

    [Fact]
    public async Task SearchAsync_WithComplexGlobPattern_MatchesCorrectly()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var file1 = tempDir + "/data.backup.txt";
        var file2 = tempDir + "/data.old.txt";
        var file3 = tempDir + "/data.new.txt";
        var file4 = tempDir + "/config.backup.txt";
        
        _mockFileSystem.AddFile(file1, "hello world");
        _mockFileSystem.AddFile(file2, "hello test");
        _mockFileSystem.AddFile(file3, "hello debug");
        _mockFileSystem.AddFile(file4, "hello sample");
        _tempFiles.Add(file1);
        _tempFiles.Add(file2);
        _tempFiles.Add(file3);
        _tempFiles.Add(file4);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("data.*.txt");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello test", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file3, 1))
            .Returns(new[] { new MatchResult(file3, 1, "hello debug", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(3, result.FileResults.Count);
        Assert.Equal(3, result.TotalMatches);
        
        // dataで始まるファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.Contains(result.FileResults, fr => fr.FileName == file2);
        Assert.Contains(result.FileResults, fr => fr.FileName == file3);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4); // config.backup.txt は除外
    }

    [Fact]
    public async Task SearchAsync_WithSpecialCharactersInGlobPattern_EscapesCorrectly()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        var file1 = tempDir + "/test(1).txt";
        var file2 = tempDir + "/test[2].txt";
        var file3 = tempDir + "/test{3}.txt";
        var file4 = tempDir + "/test1.txt";
        
        _mockFileSystem.AddFile(file1, "hello world");
        _mockFileSystem.AddFile(file2, "hello test");
        _mockFileSystem.AddFile(file3, "hello debug");
        _mockFileSystem.AddFile(file4, "hello sample");
        _tempFiles.Add(file1);
        _tempFiles.Add(file2);
        _tempFiles.Add(file3);
        _tempFiles.Add(file4);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("test(*).txt");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        Assert.Equal(1, result.TotalMatches);
        
        // test(1).txtのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file2);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file3);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4);
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
        mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(pattern);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { file }.ToList().AsReadOnly());
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(false);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns((string?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns((string?)null);
    }

    public void Dispose()
    {
        _mockFileSystem.Clear();
        _tempFiles.Clear();
    }
}