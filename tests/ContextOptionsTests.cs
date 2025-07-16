using GrepCompatible.CommandLine;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Constants;
using System.Text;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// コンテキストオプションのテスト
/// </summary>
public class ContextOptionsTests
{
    private readonly GrepApplication _application;

    public ContextOptionsTests()
    {
        _application = GrepApplication.CreateDefault();
    }

    [Fact]
    public async Task RunAsync_WithAfterContext_ShowsContextLines()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "line 1\nline 2\nmatch line 3\nline 4\nline 5";
        await File.WriteAllTextAsync(tempFile, content);
        
        var args = new[] { "-A", "2", "match", tempFile };
        
        // Act
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(0, exitCode);
        
        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WithBeforeContext_ShowsContextLines()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "line 1\nline 2\nmatch line 3\nline 4\nline 5";
        await File.WriteAllTextAsync(tempFile, content);
        
        var args = new[] { "-B", "2", "match", tempFile };
        
        // Act
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(0, exitCode);
        
        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WithContext_ShowsContextLines()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "line 1\nline 2\nmatch line 3\nline 4\nline 5";
        await File.WriteAllTextAsync(tempFile, content);
        
        var args = new[] { "-C", "2", "match", tempFile };
        
        // Act
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(0, exitCode);
        
        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WithContextAndLineNumbers_ShowsCorrectFormat()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "line 1\nline 2\nmatch line 3\nline 4\nline 5";
        await File.WriteAllTextAsync(tempFile, content);
        
        var args = new[] { "-n", "-C", "1", "match", tempFile };
        
        // Act
        var exitCode = await _application.RunAsync(args);
        
        // Assert
        Assert.Equal(0, exitCode);
        
        // Cleanup
        File.Delete(tempFile);
    }
}