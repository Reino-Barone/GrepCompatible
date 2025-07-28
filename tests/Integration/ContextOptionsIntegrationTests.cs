using GrepCompatible.Abstractions;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Core;
using GrepCompatible.Abstractions.Constants;
using Moq;
using System.Text;
using Xunit;

namespace GrepCompatible.Test.Integration;

/// <summary>
/// コンテキストオプションの統合テスト
/// </summary>
public class ContextOptionsIntegrationTests
{
    private readonly Mock<ICommand> _mockCommand = new();
    private readonly Mock<IGrepEngine> _mockEngine = new();
    private readonly Mock<IOutputFormatter> _mockFormatter = new();
    private readonly GrepApplication _application;

    public ContextOptionsIntegrationTests()
    {
        _application = new GrepApplication(_mockCommand.Object, _mockEngine.Object, _mockFormatter.Object);
    }

    [Fact]
    public async Task RunAsync_WithAfterContext_CallsEngineWithCorrectOptions()
    {
        // Arrange
        var args = new[] { "-A", "2", "match", "test.txt" };
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
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, exitCode);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithBeforeContext_CallsEngineWithCorrectOptions()
    {
        // Arrange
        var args = new[] { "-B", "2", "match", "test.txt" };
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
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, exitCode);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithContext_CallsEngineWithCorrectOptions()
    {
        // Arrange
        var args = new[] { "-C", "2", "match", "test.txt" };
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
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, exitCode);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithContextAndLineNumbers_CallsEngineWithCorrectOptions()
    {
        // Arrange
        var args = new[] { "-n", "-C", "1", "match", "test.txt" };
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
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(expectedExitCode, exitCode);
        _mockCommand.Verify(c => c.Parse(args), Times.Once);
        _mockCommand.Verify(c => c.ToOptionContext(), Times.Once);
        _mockEngine.Verify(e => e.SearchAsync(mockOptions.Object, It.IsAny<CancellationToken>()), Times.Once);
        _mockFormatter.Verify(f => f.FormatOutputAsync(searchResult, mockOptions.Object, It.IsAny<TextWriter>()), Times.Once);
    }
}
