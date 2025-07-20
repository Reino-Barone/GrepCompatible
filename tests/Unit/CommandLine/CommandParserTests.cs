using GrepCompatible.CommandLine;
using GrepCompatible.Constants;
using Xunit;

namespace GrepCompatible.Test.Unit.CommandLine;

/// <summary>
/// CommandLineパーサーの単体テスト
/// </summary>
public class CommandParserTests
{
    private readonly GrepCommand _command = new();

    [Fact]
    public void Parse_WithValidPattern_ReturnsSuccessResult()
    {
        // Arrange
        var args = new[] { "test", "file.txt" };

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void Parse_WithHelpFlag_ReturnsHelpResult()
    {
        // Arrange
        var args = new[] { "--help" };

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.ShowHelp);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Parse_WithShortHelpFlag_ReturnsHelpResult()
    {
        // Arrange
        var args = new[] { "-?" };

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void Parse_WithNoArguments_ReturnsErrorResult()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void Parse_WithIgnoreCaseFlag_SetsIgnoreCaseOption()
    {
        // Arrange
        var args = new[] { "-i", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(options.GetFlagValue(OptionNames.IgnoreCase));
    }

    [Fact]
    public void Parse_WithLineNumberFlag_SetsLineNumberOption()
    {
        // Arrange
        var args = new[] { "-n", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(options.GetFlagValue(OptionNames.LineNumber));
    }

    [Fact]
    public void Parse_WithContextOption_SetsContextValue()
    {
        // Arrange
        var args = new[] { "-C", "3", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, options.GetIntValue(OptionNames.Context));
    }

    [Fact]
    public void Parse_WithBeforeContextOption_SetsBeforeContextValue()
    {
        // Arrange
        var args = new[] { "-B", "2", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, options.GetIntValue(OptionNames.ContextBefore));
    }

    [Fact]
    public void Parse_WithAfterContextOption_SetsAfterContextValue()
    {
        // Arrange
        var args = new[] { "-A", "4", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, options.GetIntValue(OptionNames.ContextAfter));
    }

    [Fact]
    public void Parse_WithRecursiveFlag_SetsRecursiveOption()
    {
        // Arrange
        var args = new[] { "-r", "test", "." };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(options.GetFlagValue(OptionNames.RecursiveSearch));
    }

    [Fact]
    public void Parse_WithFixedStringsFlag_SetsFixedStringsOption()
    {
        // Arrange
        var args = new[] { "-F", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(options.GetFlagValue(OptionNames.FixedStrings));
    }

    [Fact]
    public void Parse_WithWholeWordFlag_SetsWholeWordOption()
    {
        // Arrange
        var args = new[] { "-w", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(options.GetFlagValue(OptionNames.WholeWord));
    }

    [Fact]
    public void Parse_WithMultipleFiles_SetsFilesArgument()
    {
        // Arrange
        var args = new[] { "test", "file1.txt", "file2.txt", "file3.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        var files = options.GetStringListArgumentValue(ArgumentNames.Files);
        Assert.NotNull(files);
        Assert.Equal(3, files.Count);
        Assert.Contains("file1.txt", files);
        Assert.Contains("file2.txt", files);
        Assert.Contains("file3.txt", files);
    }

    [Fact]
    public void Parse_WithPattern_SetsPatternArgument()
    {
        // Arrange
        var args = new[] { "hello.*world", "file.txt" };

        // Act
        var result = _command.Parse(args);
        var options = _command.ToOptionContext();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hello.*world", options.GetStringArgumentValue(ArgumentNames.Pattern));
    }

    [Fact]
    public void Parse_WithInvalidContextValue_ReturnsErrorResult()
    {
        // Arrange
        var args = new[] { "-C", "invalid", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_WithNegativeContextValue_ReturnsErrorResult()
    {
        // Arrange
        var args = new[] { "-C", "-1", "test", "file.txt" };

        // Act
        var result = _command.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void GetHelpText_ReturnsFormattedHelpText()
    {
        // Act
        var helpText = _command.GetHelpText();

        // Assert
        Assert.NotNull(helpText);
        Assert.Contains("Usage:", helpText);
        Assert.Contains("Options:", helpText);
        Assert.Contains("PATTERN", helpText);
    }
}
