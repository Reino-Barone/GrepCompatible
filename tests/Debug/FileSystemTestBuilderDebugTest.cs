using System;
using System.Linq;
using System.Threading.Tasks;
using GrepCompatible.Test.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace GrepCompatible.Test.Debug
{
    public class FileSystemTestBuilderDebugTest
    {
        private readonly ITestOutputHelper _output;

        public FileSystemTestBuilderDebugTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task DebugFileSystemTestBuilder_ShouldShowActualContent()
        {
            // Arrange
            var testContent = "line1\ntest line\nline3\ntest again\nline5";
            var fileSystemBuilder = new FileSystemTestBuilder();
            var fileSystem = fileSystemBuilder
                .WithFile("test.txt", testContent)
                .Build();

            // Act
            _output.WriteLine($"Original content: {testContent}");
            
            var fileInfo = fileSystem.GetFileInfo("test.txt");
            _output.WriteLine($"FileInfo exists: {fileInfo != null}");
            
            if (fileInfo != null)
            {
                _output.WriteLine($"FileInfo.Exists: {fileInfo.Exists}");
                _output.WriteLine($"FileInfo.Name: {fileInfo.Name}");
                _output.WriteLine($"FileInfo.FullName: {fileInfo.FullName}");
                _output.WriteLine($"FileInfo.Length: {fileInfo.Length}");
            }

            try
            {
                var lines = await fileSystem.ReadLinesAsync("test.txt");
                var lineList = await lines.ToListAsync();
                
                _output.WriteLine($"Number of lines read: {lineList.Count}");
                for (int i = 0; i < lineList.Count; i++)
                {
                    _output.WriteLine($"Line {i}: '{lineList[i]}'");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception reading lines: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            // Assert
            Assert.NotNull(fileInfo);
        }
    }
}
