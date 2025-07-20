using System.Threading.Tasks;
using GrepCompatible.Test.Infrastructure;
using Xunit;

namespace GrepCompatible.Test;

public class DebugFileSystemTest
{
    [Fact]
    public async Task FileSystemTestBuilder_WithFile_ShouldWork()
    {
        // Arrange
        var testContent = "Hello World\nThis is a test";
        var expectedBytes = System.Text.Encoding.UTF8.GetByteCount(testContent);
        
        // Debug: 実際の文字数とバイト数を確認
        var actualChars = testContent.Length;
        Console.WriteLine($"Characters: {actualChars}, UTF-8 bytes: {expectedBytes}");
        
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("test.txt", testContent)
            .Build();

        // Act & Assert - ファイルが存在することを確認
        var fileInfo = fileSystem.GetFileInfo("test.txt");
        Assert.True(fileInfo.Exists);
        
        // Debug: 実際に返される値を確認
        Console.WriteLine($"FileInfo.Length: {fileInfo.Length}");
        Assert.Equal(expectedBytes, fileInfo.Length); // UTF-8バイト数で比較

        // ファイルを列挙できることを確認
        var files = fileSystem.EnumerateFiles(".", "*", System.IO.SearchOption.TopDirectoryOnly);
        Assert.Contains("test.txt", files);

        // ファイル内容を読み取れることを確認
        using var reader = fileSystem.OpenText("test.txt", System.Text.Encoding.UTF8, 4096);
        var content = await reader.ReadToEndAsync();
        Assert.Equal(testContent, content);
    }
}
