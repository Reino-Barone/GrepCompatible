using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using GrepCompatible.Test.Infrastructure;
using Moq;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// GrepEngineの統合テスト（簡易版）
/// </summary>
public class GrepEngineDebugTests : IDisposable
{
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly MockPathHelper _mockPathHelper = new();
    private readonly ParallelGrepEngine _engine;

    public GrepEngineDebugTests()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(_mockStrategyFactory.Object, _mockFileSystem, _mockPathHelper);
    }

    [Fact]
    public async Task Debug_SearchAsync_WithSingleFile_CheckFlow()
    {
        // Arrange
        var testFile = "test.txt";
        var testContent = "hello world\ntest line\nhello again";
        _mockFileSystem.AddFile(testFile, testContent);
        
        // ファイルが存在することを確認
        Assert.True(_mockFileSystem.FileExists(testFile));
        
        // ファイルを開けることを確認
        using var reader = _mockFileSystem.OpenText(testFile, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Equal(testContent, content);
        
        var mockOptions = new Mock<IOptionContext>();
        mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns("hello");
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { testFile }.ToList().AsReadOnly());
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(false);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns((string?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns((string?)null);
        
        // マッチストラテジーのモックを設定
        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, testFile, 1))
            .Returns(new[] { new MatchResult(testFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("test line", "hello", mockOptions.Object, testFile, 2))
            .Returns(Array.Empty<MatchResult>());
        _mockStrategy.Setup(s => s.FindMatches("hello again", "hello", mockOptions.Object, testFile, 3))
            .Returns(new[] { new MatchResult(testFile, 3, "hello again", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.NotNull(result);
        
        // デバッグ情報を出力
        // Debug output removed to keep test logs clean.
        
        if (result.FileResults.Count == 0)
        {
            Assert.Fail("No file results found");
        }
        
        Assert.Single(result.FileResults);
        
        var fileResult = result.FileResults[0];
        Assert.Equal(testFile, fileResult.FileName);
        Assert.Equal(2, fileResult.TotalMatches);
        Assert.True(fileResult.HasMatches);
        Assert.False(fileResult.HasError);
        
        // マッチストラテジーが呼ばれたかを確認
        _mockStrategy.Verify(s => s.FindMatches("hello world", "hello", mockOptions.Object, testFile, 1), Times.Once);
        _mockStrategy.Verify(s => s.FindMatches("test line", "hello", mockOptions.Object, testFile, 2), Times.Once);
        _mockStrategy.Verify(s => s.FindMatches("hello again", "hello", mockOptions.Object, testFile, 3), Times.Once);
    }

    public void Dispose()
    {
        _mockFileSystem.Clear();
    }
}
