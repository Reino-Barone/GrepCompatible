using GrepCompatible.Constants;
using GrepCompatible.Core;
using GrepCompatible.Models;
using Moq;
using System.Text;
using Xunit;

namespace GrepCompatible.Test;

public class OutputFormatterTests
{
    private readonly Mock<IOptionContext> _mockOptions = new();
    private readonly PosixOutputFormatter _formatter = new();
    private readonly StringWriter _writer = new();

    [Fact]
    public async Task FormatOutputAsync_SilentMode_ReturnsCorrectExitCode()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 1);
        var searchResult = new SearchResult(new[] { fileResult }, 1, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(_writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_SilentModeNoMatches_ReturnsCorrectExitCode()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(true);
        
        var fileResult = new FileResult("test.txt", new List<MatchResult>(), 0);
        var searchResult = new SearchResult(new[] { fileResult }, 0, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(_writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_CountOnly_SingleFile_ShowsCountOnly()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(true);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "test.txt" }.ToList().AsReadOnly());
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("2", lines[0]);
    }

    [Fact]
    public async Task FormatOutputAsync_CountOnly_MultipleFiles_ShowsFilenameAndCount()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(true);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
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
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("test1.txt:1", lines);
        Assert.Contains("test2.txt:2", lines);
    }

    [Fact]
    public async Task FormatOutputAsync_FilenameOnly_ShowsMatchingFilenames()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var noMatches = new List<MatchResult>();
        
        var fileResult1 = new FileResult("test.txt", matches, 1);
        var fileResult2 = new FileResult("empty.txt", noMatches, 0);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 1, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("test.txt", lines[0]);
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_SingleFile_NoLineNumbers()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: true, showLineNumbers: false);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("hello world", lines);
        Assert.Contains("hello again", lines);
        Assert.DoesNotContain("test.txt:", output);
        Assert.All(lines, line => Assert.DoesNotContain(":", line));
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_SingleFile_WithLineNumbers()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: true, showLineNumbers: true);
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5),
            new("test.txt", 2, "hello again", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 2);
        var searchResult = new SearchResult(new[] { fileResult }, 2, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("1:hello world", lines);
        Assert.Contains("2:hello again", lines);
        Assert.DoesNotContain("test.txt:", output);
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_MultipleFiles_WithFilenameAndLineNumbers()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: false, showLineNumbers: true);
        
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
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("test1.txt:1:hello world", lines);
        Assert.Contains("test2.txt:3:hello again", lines);
    }

    [Fact]
    public async Task FormatOutputAsync_NormalOutput_MultipleFiles_WithFilenameOnly()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: false, showLineNumbers: false);
        
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
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("test1.txt:hello world", lines);
        Assert.Contains("test2.txt:hello again", lines);
    }

    [Fact]
    public async Task FormatOutputAsync_SuppressFilename_OverridesFilenameDisplay()
    {
        // Arrange
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(true);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.LineNumber)).Returns(true);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { "test1.txt", "test2.txt" }.ToList().AsReadOnly());
        
        var matches = new List<MatchResult>
        {
            new("test.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        
        var fileResult = new FileResult("test.txt", matches, 1);
        var searchResult = new SearchResult(new[] { fileResult }, 1, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("1:hello world", lines[0]);
        Assert.DoesNotContain("test.txt:", output);
    }

    [Fact]
    public async Task FormatOutputAsync_NoMatches_ReturnsExitCode1()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: true, showLineNumbers: false);
        
        var fileResult = new FileResult("test.txt", new List<MatchResult>(), 0);
        var searchResult = new SearchResult(new[] { fileResult }, 0, 1, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(_writer.ToString());
    }

    [Fact]
    public async Task FormatOutputAsync_SkipsFilesWithoutMatches()
    {
        // Arrange
        SetupNormalOutputOptions(singleFile: false, showLineNumbers: false);
        
        var matchesFile1 = new List<MatchResult>
        {
            new("test1.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var noMatchesFile2 = new List<MatchResult>();
        
        var fileResult1 = new FileResult("test1.txt", matchesFile1, 1);
        var fileResult2 = new FileResult("test2.txt", noMatchesFile2, 0);
        var searchResult = new SearchResult(new[] { fileResult1, fileResult2 }, 1, 2, TimeSpan.FromMilliseconds(100));

        // Act
        var exitCode = await _formatter.FormatOutputAsync(searchResult, _mockOptions.Object, _writer);

        // Assert
        Assert.Equal(0, exitCode);
        var output = _writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("test1.txt:hello world", lines[0]);
        Assert.DoesNotContain("test2.txt", output);
    }

    private void SetupNormalOutputOptions(bool singleFile, bool showLineNumbers)
    {
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SilentMode)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.CountOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FilenameOnly)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.SuppressFilename)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.LineNumber)).Returns(showLineNumbers);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        _mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        
        if (singleFile)
        {
            _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new[] { "test.txt" }.ToList().AsReadOnly());
        }
        else
        {
            _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new[] { "test1.txt", "test2.txt" }.ToList().AsReadOnly());
        }
    }
}
