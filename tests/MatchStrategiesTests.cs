using GrepCompatible.Constants;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using Moq;
using Xunit;

namespace GrepCompatible.Test;

public class MatchStrategiesTests
{
    private readonly Mock<IOptionContext> _mockOptions = new();

    [Fact]
    public void FixedStringMatchStrategy_CanApply_ReturnsTrueWhenFixedStringsOptionIsSet()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(true);

        // Act
        var result = strategy.CanApply(_mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void FixedStringMatchStrategy_CanApply_ReturnsFalseWhenFixedStringsOptionIsNotSet()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(false);

        // Act
        var result = strategy.CanApply(_mockOptions.Object);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_FindsSingleMatch()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        var line = "hello world";
        var pattern = "world";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("test.txt", matches[0].FileName);
        Assert.Equal(1, matches[0].LineNumber);
        Assert.Equal(line, matches[0].Line);
        Assert.Equal("world", matches[0].MatchedText.ToString());
        Assert.Equal(6, matches[0].StartIndex);
        Assert.Equal(11, matches[0].EndIndex);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_FindsMultipleMatches()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        var line = "test test test";
        var pattern = "test";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Equal(3, matches.Length);
        Assert.Equal(0, matches[0].StartIndex);
        Assert.Equal(5, matches[1].StartIndex);
        Assert.Equal(10, matches[2].StartIndex);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_IgnoreCaseOption()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        var line = "Hello WORLD";
        var pattern = "world";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(true);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("WORLD", matches[0].MatchedText.ToString());
        Assert.Equal(6, matches[0].StartIndex);
        Assert.Equal(11, matches[0].EndIndex);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_EmptyPattern_ReturnsEmpty()
    {
        // Arrange
        var strategy = new FixedStringMatchStrategy();
        var line = "hello world";
        var pattern = "";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Empty(matches);
    }

    [Fact]
    public void RegexMatchStrategy_CanApply_ReturnsTrueWhenExtendedRegexpOptionIsSet()
    {
        // Arrange
        var strategy = new RegexMatchStrategy();
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.ExtendedRegexp)).Returns(true);

        // Act
        var result = strategy.CanApply(_mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegexMatchStrategy_CanApply_ReturnsTrueWhenNoSpecialOptionsSet()
    {
        // Arrange
        var strategy = new RegexMatchStrategy();
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.ExtendedRegexp)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(false);
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.WholeWord)).Returns(false);

        // Act
        var result = strategy.CanApply(_mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegexMatchStrategy_FindMatches_SimpleRegexPattern()
    {
        // Arrange
        var strategy = new RegexMatchStrategy();
        var line = "hello 123 world";
        var pattern = @"\d+";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("123", matches[0].MatchedText.ToString());
        Assert.Equal(6, matches[0].StartIndex);
        Assert.Equal(9, matches[0].EndIndex);
    }

    [Fact]
    public void RegexMatchStrategy_FindMatches_InvalidRegexPattern_TreatsAsFixedString()
    {
        // Arrange
        var strategy = new RegexMatchStrategy();
        var line = "hello [world";
        var pattern = "[world";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("[world", matches[0].MatchedText.ToString());
        Assert.Equal(6, matches[0].StartIndex);
        Assert.Equal(12, matches[0].EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_CanApply_ReturnsTrueWhenWholeWordOptionIsSet()
    {
        // Arrange
        var strategy = new WholeWordMatchStrategy();
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.WholeWord)).Returns(true);

        // Act
        var result = strategy.CanApply(_mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_MatchesWholeWordsOnly()
    {
        // Arrange
        var strategy = new WholeWordMatchStrategy();
        var line = "hello world helloworld";
        var pattern = "hello";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("hello", matches[0].MatchedText.ToString());
        Assert.Equal(0, matches[0].StartIndex);
        Assert.Equal(5, matches[0].EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_IgnoreCaseOption()
    {
        // Arrange
        var strategy = new WholeWordMatchStrategy();
        var line = "Hello WORLD";
        var pattern = "hello";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(true);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        Assert.Equal("Hello", matches[0].MatchedText.ToString());
        Assert.Equal(0, matches[0].StartIndex);
        Assert.Equal(5, matches[0].EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_EmptyPattern_ReturnsEmpty()
    {
        // Arrange
        var strategy = new WholeWordMatchStrategy();
        var line = "hello world";
        var pattern = "";
        _mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, _mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Empty(matches);
    }
}
