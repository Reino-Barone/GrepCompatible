using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Core;
using Moq;
using System.Text;
using Xunit;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// OutputFormatterクラスの単体テスト
/// </summary>
public class OutputFormatterTests
{
    private readonly PosixOutputFormatter _formatter = new();

    [Fact]
    public async Task FormatOutputAsync_SilentMode_ReturnsCorrectExitCode()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 1);
        var searchResult = new SearchResult(new[] { fileResult }, 1, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_SilentModeNoMatches_ReturnsCorrectExitCode()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(true);
        
        var fileResult = new FileResult("test.txt", new List<MatchResult>(), 0);
        var searchResult = new SearchResult(new[] { fileResult }, 0, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_CountOnly_SingleFile_ShowsCountOnly()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(true);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "test.txt" }.ToList().AsReadOnly());
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("2", lines[0]);
    }

    [Fact]
    public async Task FormatOutputAsync_CountOnly_MultipleFiles_ShowsFilenameAndCount()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(true);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "test1.txt", "test2.txt" }.ToList().AsReadOnly());
        
        var matches1 = new List<MatchResult>
        {
            new("test1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var matches2 = new List<MatchResult>
        {
            new("test2.txt", 1, "hello again", "hello".AsMemory(), 0, 5),
            new("test2.txt", 2, "hello once more", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult1 = new FileResult("test1.txt", matches1, 1);
        var fileResult2 = new FileResult("test2.txt", matches2, 2);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 3, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // 順序に依存しないアサーション
        Assert.Contains(lines, line => line == "test1.txt:1");
        Assert.Contains(lines, line => line == "test2.txt:2");
    }

    [Fact]
    public async Task FormatOutputAsync_FilenameOnly_ShowsMatchingFilenames()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var noMatches = new List<MatchResult>();
        
        var fileResult1 = new FileResult("test.txt", matches, 1);
        var fileResult2 = new FileResult("empty.txt", noMatches, 0);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 1, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("test.txt", lines[0]);
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_SingleFile_NoLineNumbers()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: true, showLineNumbers: false);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // 順序に依存しないアサーション
        Assert.Contains(lines, line => line == "hello world");
        Assert.Contains(lines, line => line == "hello again");
        Assert.DoesNotContain("test.txt:", output);
        Assert.All(lines, line => Assert.DoesNotContain(":", line));
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_SingleFile_WithLineNumbers()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: true, showLineNumbers: true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // 順序に依存しないアサーション
        Assert.Contains(lines, line => line == "1:hello world");
        Assert.Contains(lines, line => line == "2:hello again");
        Assert.DoesNotContain("test.txt:", output);
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_MultipleFiles_WithFilenameAndLineNumbers()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: false, showLineNumbers: true);
        
        var matches1 = new List<MatchResult>
        {
            new("test1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var matches2 = new List<MatchResult>
        {
            new("test2.txt", 3, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult1 = new FileResult("test1.txt", matches1, 1);
        var fileResult2 = new FileResult("test2.txt", matches2, 1);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 2, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // 順序に依存しないアサーション
        Assert.Contains(lines, line => line == "test1.txt:1:hello world");
        Assert.Contains(lines, line => line == "test2.txt:3:hello again");
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_MultipleFiles_WithFilenameOnly()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: false, showLineNumbers: false);
        
        var matches1 = new List<MatchResult>
        {
            new("test1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var matches2 = new List<MatchResult>
        {
            new("test2.txt", 3, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult1 = new FileResult("test1.txt", matches1, 1);
        var fileResult2 = new FileResult("test2.txt", matches2, 1);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 2, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // 順序に依存しないアサーション
        Assert.Contains(lines, line => line == "test1.txt:hello world");
        Assert.Contains(lines, line => line == "test2.txt:hello again");
    }

    [Fact]
    public async Task FormatOutputAsync_SuppressFilename_OverridesFilenameDisplay()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(true);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.LineNumber)).Returns(true);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "test1.txt", "test2.txt" }.ToList().AsReadOnly());
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 1);
        var searchResult = new SearchResult(new[] { fileResult }, 1, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("1:hello world", lines[0]);
        Assert.DoesNotContain("test.txt:", output);
    }

    [Fact]
    public async Task FormatOutputAsync_NoMatches_ReturnsExitCode1()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: true, showLineNumbers: false);
        
        var fileResult = new FileResult("test.txt", new List<MatchResult>(), 0);
        var searchResult = new SearchResult(new[] { fileResult }, 0, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_SkipsFilesWithoutMatches()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        SetupNormalOutputOptions(mockOptions, singleFile: false, showLineNumbers: false);
        
        var matchesFile1 = new List<MatchResult>
        {
            new("test1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var noMatchesFile2 = new List<MatchResult>();
        
        var fileResult1 = new FileResult("test1.txt", matchesFile1, 1);
        var fileResult2 = new FileResult("test2.txt", noMatchesFile2, 0);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 1, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("test1.txt:hello world", lines[0]);
        Assert.DoesNotContain("test2.txt", output);
    }

    private static void SetupNormalOutputOptions(Mock<IOptionContext> mockOptions, bool singleFile, bool showLineNumbers)
    {
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.LineNumber)).Returns(showLineNumbers);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        
        if (singleFile)
        {
            mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new[] { "test.txt" }.ToList().AsReadOnly());
        }
        else
        {
            mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new[] { "test1.txt", "test2.txt" }.ToList().AsReadOnly());
        }
    }

    [Fact]
    public async Task FormatOutputAsync_RecursiveSearch_MultipleFiles_ShouldShowFilenames()
    {
        // Arrange: Simulate recursive search with single directory argument but multiple files found
        var mockOptions = new Mock<IOptionContext>();
        var writer = new StringWriter();
        
        // Setup options to simulate recursive search with single directory argument
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.LineNumber)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        
        // This simulates the original command line arguments: just the directory
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "/path/to/directory" }.ToList().AsReadOnly());
        
        // Create multiple file results (what actually gets searched)
        var matches1 = new List<MatchResult>
        {
            new("/path/to/directory/file1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var matches2 = new List<MatchResult>
        {
            new("/path/to/directory/file2.txt", 1, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult1 = new FileResult("/path/to/directory/file1.txt", matches1, 1);
        var fileResult2 = new FileResult("/path/to/directory/file2.txt", matches2, 1);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 2, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, mockOptions.Object, writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        
        // With the current bug, filenames won't be shown because files.Count = 1
        // But we EXPECT them to be shown because we're searching multiple files
        Assert.Contains(lines, line => line == "/path/to/directory/file1.txt:hello world");
        Assert.Contains(lines, line => line == "/path/to/directory/file2.txt:hello again");
    }
}
