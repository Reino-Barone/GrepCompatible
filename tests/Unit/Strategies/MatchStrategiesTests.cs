using GrepCompatible.Abstractions;
using GrepCompatible.Constants;
using GrepCompatible.Core;
using GrepCompatible.Strategies;
using Moq;
using Xunit;

namespace GrepCompatible.Test.Unit.Strategies;

/// <summary>
/// MatchStrategiesクラス群の単体テスト
/// </summary>
public class MatchStrategiesTests
{
    [Fact]
    public void FixedStringMatchStrategy_CanApply_ReturnsTrueWhenFixedStringsOptionIsSet()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(true);

        // Act
        var result = strategy.CanApply(mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void FixedStringMatchStrategy_CanApply_ReturnsFalseWhenFixedStringsOptionIsNotSet()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(false);

        // Act
        var result = strategy.CanApply(mockOptions.Object);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_FindsSingleMatch()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        var line = "hello world";
        var pattern = "world";
        var expectedStartIndex = line.IndexOf(pattern);
        var expectedEndIndex = expectedStartIndex + pattern.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal("test.txt", match.FileName);
        Assert.Equal(1, match.LineNumber);
        Assert.Equal(line, match.Line);
        Assert.Equal(pattern, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_FindsMultipleMatches()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        var line = "test test test";
        var pattern = "test";
        var expectedPositions = new[] { 0, 5, 10 };
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Equal(3, matches.Length);
        for (int i = 0; i < matches.Length; i++)
        {
            Assert.Equal(expectedPositions[i], matches[i].StartIndex);
            Assert.Equal(expectedPositions[i] + pattern.Length, matches[i].EndIndex);
            Assert.Equal(pattern, matches[i].MatchedText.ToString());
        }
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_IgnoreCaseOption()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        var line = "Hello WORLD";
        var pattern = "world";
        var expectedMatch = "WORLD";
        var expectedStartIndex = line.IndexOf(expectedMatch, StringComparison.OrdinalIgnoreCase);
        var expectedEndIndex = expectedStartIndex + expectedMatch.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(true);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal(expectedMatch, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void FixedStringMatchStrategy_FindMatches_EmptyPattern_ReturnsEmpty()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new FixedStringMatchStrategy();
        var line = "hello world";
        var pattern = "";
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Empty(matches);
    }

    [Fact]
    public void RegexMatchStrategy_CanApply_ReturnsTrueWhenExtendedRegexpOptionIsSet()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new RegexMatchStrategy();
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.ExtendedRegexp)).Returns(true);

        // Act
        var result = strategy.CanApply(mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegexMatchStrategy_CanApply_ReturnsTrueWhenNoSpecialOptionsSet()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new RegexMatchStrategy();
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.ExtendedRegexp)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.FixedStrings)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.WholeWord)).Returns(false);

        // Act
        var result = strategy.CanApply(mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegexMatchStrategy_FindMatches_SimpleRegexPattern()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new RegexMatchStrategy();
        var line = "hello 123 world";
        var pattern = @"\d+";
        var expectedMatch = "123";
        var expectedStartIndex = line.IndexOf(expectedMatch);
        var expectedEndIndex = expectedStartIndex + expectedMatch.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal(expectedMatch, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void RegexMatchStrategy_FindMatches_InvalidRegexPattern_TreatsAsFixedString()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new RegexMatchStrategy();
        var line = "hello [world";
        var pattern = "[world";
        var expectedMatch = "[world";
        var expectedStartIndex = line.IndexOf(expectedMatch);
        var expectedEndIndex = expectedStartIndex + expectedMatch.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal(expectedMatch, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_CanApply_ReturnsTrueWhenWholeWordOptionIsSet()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new WholeWordMatchStrategy();
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.WholeWord)).Returns(true);

        // Act
        var result = strategy.CanApply(mockOptions.Object);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_MatchesWholeWordsOnly()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new WholeWordMatchStrategy();
        var line = "hello world helloworld";
        var pattern = "hello";
        var expectedMatch = "hello";
        var expectedStartIndex = line.IndexOf(expectedMatch);
        var expectedEndIndex = expectedStartIndex + expectedMatch.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal(expectedMatch, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_IgnoreCaseOption()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new WholeWordMatchStrategy();
        var line = "Hello WORLD";
        var pattern = "hello";
        var expectedMatch = "Hello";
        var expectedStartIndex = line.IndexOf(expectedMatch, StringComparison.OrdinalIgnoreCase);
        var expectedEndIndex = expectedStartIndex + expectedMatch.Length;
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(true);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal(expectedMatch, match.MatchedText.ToString());
        Assert.Equal(expectedStartIndex, match.StartIndex);
        Assert.Equal(expectedEndIndex, match.EndIndex);
    }

    [Fact]
    public void WholeWordMatchStrategy_FindMatches_EmptyPattern_ReturnsEmpty()
    {
        // Arrange
        var mockOptions = new Mock<IOptionContext>();
        var strategy = new WholeWordMatchStrategy();
        var line = "hello world";
        var pattern = "";
        
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.IgnoreCase)).Returns(false);

        // Act
        var matches = strategy.FindMatches(line, pattern, mockOptions.Object, "test.txt", 1).ToArray();

        // Assert
        Assert.Empty(matches);
    }
}
