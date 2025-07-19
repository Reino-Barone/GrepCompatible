using System.Linq;
using GrepCompatible.Test.Infrastructure;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// MockFileSystemのテスト
/// </summary>
public class MockFileSystemTests
{
    [Fact]
    public void MockFileSystem_FileExists_ReturnsTrue_WhenFileAdded()
    {
        // Arrange
        var mockFs = new MockFileSystem();
        const string testFile = "test.txt";
        
        // Act
        mockFs.AddFile(testFile, "test content");
        
        // Assert
        Assert.True(mockFs.FileExists(testFile));
    }
    
    [Fact]
    public void MockFileSystem_OpenText_ReturnsCorrectContent()
    {
        // Arrange
        var mockFs = new MockFileSystem();
        const string testFile = "test.txt";
        const string testContent = "test content\nsecond line";
        
        // Act
        mockFs.AddFile(testFile, testContent);
        
        // Assert
        using var reader = mockFs.OpenText(testFile, System.Text.Encoding.UTF8);
        var content = reader.ReadToEnd();
        Assert.Equal(testContent, content);
    }
    
    [Fact]
    public void MockFileSystem_EnumerateFiles_ReturnsFilesInDirectory()
    {
        // Arrange
        var mockFs = new MockFileSystem();
        const string testDir = "testdir";
        const string testFile1 = "testdir/test1.txt";
        const string testFile2 = "testdir/test2.txt";
        
        // Act
        mockFs.AddDirectory(testDir);
        mockFs.AddFile(testFile1, "content1");
        mockFs.AddFile(testFile2, "content2");
        
        // Assert
        var files = mockFs.EnumerateFiles(testDir, "*", System.IO.SearchOption.TopDirectoryOnly).ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains(testFile1, files);
        Assert.Contains(testFile2, files);
    }
}
