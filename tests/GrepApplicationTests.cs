using GrepCompatible.CommandLine;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using Moq;
using System.Text;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// GrepApplicationクラスのテスト
/// </summary>
public class GrepApplicationTests
{
    private readonly Mock<ICommand> _mockCommand = new();
    private readonly Mock<IGrepEngine> _mockEngine = new();
    private readonly Mock<IOutputFormatter> _mockFormatter = new();
    private readonly GrepApplication _application;

    public GrepApplicationTests()
    {
        _application = new GrepApplication(_mockCommand.Object, _mockEngine.Object, _mockFormatter.Object);
    }

    [Fact]
    public async Task RunAsync_WithNoArguments_ReturnsErrorCode()
    {
        // Arrange
        var args = Array.Empty<string>();
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_WithHelpFlag_ReturnsSuccessCode()
    {
        // Arrange
        var args = new[] { "--help" };
        var helpText = "Usage: grep [OPTIONS] PATTERN [FILE...]";
        
        var parseResult = CommandParseResult.Help();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.GetHelpText()).Returns(helpText);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(0, result);
        _mockCommand.Verify(c => c.GetHelpText(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithInvalidArguments_ReturnsErrorCode()
    {
        // Arrange
        var args = new[] { "invalid", "args" };
        var errorMessage = "Invalid arguments";
        
        var parseResult = CommandParseResult.Error(errorMessage);
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_WithValidArguments_CallsEngineAndFormatter()
    {
        // Arrange
        var args = new[] { "pattern", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        var searchResult = new SearchResult([], 0, 0, TimeSpan.FromMilliseconds(100));
        const int expectedExitCode = 0;
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        _mockFormatter.Setup(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()))
            .ReturnsAsync(expectedExitCode);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, result);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithCancellation_ReturnsSignalExitCode()
    {
        // Arrange
        var args = new[] { "pattern", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(130, result); // SIGINT終了コード
    }

    [Fact]
    public async Task RunAsync_WithException_ReturnsErrorCode()
    {
        // Arrange
        var args = new[] { "pattern", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_WithSearchMatches_ReturnsFormatterExitCode()
    {
        // Arrange
        var args = new[] { "hello", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        var matches = new[]
        {
            new MatchResult("file.txt", 1, "hello world", "hello".AsMemory(), 0, 5)
        };
        var fileResults = new[]
        {
            new FileResult("file.txt", matches.AsReadOnly(), 1)
        };
        var searchResult = new SearchResult(fileResults, 1, 1, TimeSpan.FromMilliseconds(50));
        const int expectedExitCode = 0;
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        _mockFormatter.Setup(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()))
            .ReturnsAsync(expectedExitCode);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, result);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithNoMatches_ReturnsFormatterExitCode()
    {
        // Arrange
        var args = new[] { "nonexistent", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        var searchResult = new SearchResult([], 0, 0, TimeSpan.FromMilliseconds(25));
        const int expectedExitCode = 1;
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        _mockFormatter.Setup(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()))
            .ReturnsAsync(expectedExitCode);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, result);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public void Constructor_WithNullCommand_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GrepApplication(null!, _mockEngine.Object, _mockFormatter.Object));
    }

    [Fact]
    public void Constructor_WithNullEngine_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GrepApplication(_mockCommand.Object, null!, _mockFormatter.Object));
    }

    [Fact]
    public void Constructor_WithNullFormatter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GrepApplication(_mockCommand.Object, _mockEngine.Object, null!));
    }

    [Fact]
    public void CreateDefault_ReturnsConfiguredApplication()
    {
        // Act
        var application = GrepApplication.CreateDefault();
        
        // Assert
        Assert.NotNull(application);
        Assert.IsType<GrepApplication>(application);
    }

    [Fact]
    public async Task RunAsync_WithComplexArguments_ProcessesCorrectly()
    {
        // Arrange
        var args = new[] { "-i", "-n", "pattern", "file1.txt", "file2.txt" };
        var mockOptions = new Mock<IOptionContext>();
        var matches = new[]
        {
            new MatchResult("file1.txt", 1, "PATTERN found", "PATTERN".AsMemory(), 0, 7),
            new MatchResult("file2.txt", 3, "another pattern", "pattern".AsMemory(), 8, 7)
        };
        var fileResults = new[]
        {
            new FileResult("file1.txt", new[] { matches[0] }.AsReadOnly(), 1),
            new FileResult("file2.txt", new[] { matches[1] }.AsReadOnly(), 1)
        };
        var searchResult = new SearchResult(fileResults, 2, 2, TimeSpan.FromMilliseconds(75));
        const int expectedExitCode = 0;
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        _mockFormatter.Setup(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()))
            .ReturnsAsync(expectedExitCode);
        
        // Act
        var result = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, result);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithCancellationToken_PassesToEngine()
    {
        // Arrange
        var args = new[] { "pattern", "file.txt" };
        var mockOptions = new Mock<IOptionContext>();
        var searchResult = new SearchResult([], 0, 0, TimeSpan.FromMilliseconds(10));
        var cancellationToken = new CancellationToken();
        
        var parseResult = CommandParseResult.Success();
        _mockCommand.Setup(c => c.Parse(args)).Returns(parseResult);
        _mockCommand.Setup(c => c.ToOptionContext()).Returns(mockOptions.Object);
        _mockEngine.Setup(e => e.SearchAsync(mockOptions.Object, cancellationToken))
            .ReturnsAsync(searchResult);
        _mockFormatter.Setup(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()))
            .ReturnsAsync(0);
        
        // Act
        var result = await _application.RunAsync(args, cancellationToken);
        
        // Assert
        Assert.Equal(0, result);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, cancellationToken), Times.Once);
    }
}
