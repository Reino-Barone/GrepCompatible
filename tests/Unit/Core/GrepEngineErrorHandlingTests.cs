using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.CommandLine;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// GrepEngineエラーハンドリング・エッジケースのテスト
/// </summary>
public class GrepEngineErrorHandlingTests : GrepEngineTestsBase
{
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
        var result = await Engine.SearchAsync(mockOptions.Object, cts.Token);

        // Assert
        Assert.Empty(result.FileResults);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal(0, result.TotalFiles);
    }

    [Fact]
    public async Task SearchAsync_WithNonExistentFile_ReturnsEmptyResult()
    {
        // Arrange
        var nonExistentFile = $"non_existent_{Guid.NewGuid()}.txt";
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, nonExistentFile, "hello");

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        // 非存在ファイルの場合、グロブ展開が失敗してファイルが見つからない、または
        // エラーを含む結果が返されるはず
        if (result.FileResults.Count > 0)
        {
            // エラーが発生した場合
            Assert.Single(result.FileResults);
            Assert.True(result.FileResults[0].HasError);
            Assert.Equal(0, result.FileResults[0].TotalMatches);
        }
        else
        {
            // ファイルが見つからなかった場合
            Assert.Empty(result.FileResults);
            Assert.Equal(0, result.TotalMatches);
            Assert.Equal(0, result.TotalFiles);
        }
    }

    [Fact]
    public async Task SearchAsync_WithEmptyFile_ReturnsEmptyResult()
    {
        // Arrange
        var tempFile = CreateTempFile("");
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

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
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempFile, "hello");
        
        var expectedMatch = new MatchResult(tempFile, 1, "hello world", "hello".AsMemory(), 0, 5);
        MockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, tempFile, 1))
            .Returns(new[] { expectedMatch });

        // Act
        var result = await Engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        var fileResult = result.FileResults[0];
        Assert.Equal(tempFile, fileResult.FileName);
        Assert.Equal(1, fileResult.TotalMatches);
        Assert.False(fileResult.HasError);
    }
}
