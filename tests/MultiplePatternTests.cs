using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Constants;
using GrepCompatible.Abstractions;
using GrepCompatible.Strategies;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GrepCompatible.Tests
{
    public class MultiplePatternTests
    {
        private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory;
        private readonly Mock<IFileSystem> _mockFileSystem;
        private readonly Mock<IPath> _mockPath;
        private readonly Mock<IFileSearchService> _mockFileSearchService;
        private readonly Mock<IPerformanceOptimizer> _mockPerformanceOptimizer;
        private readonly Mock<IMatchResultPool> _mockMatchResultPool;
        private readonly Mock<IOptionContext> _mockOptions;
        private readonly ParallelGrepEngine _engine;

        public MultiplePatternTests()
        {
            _mockStrategyFactory = new Mock<IMatchStrategyFactory>();
            _mockFileSystem = new Mock<IFileSystem>();
            _mockPath = new Mock<IPath>();
            _mockFileSearchService = new Mock<IFileSearchService>();
            _mockPerformanceOptimizer = new Mock<IPerformanceOptimizer>();
            _mockMatchResultPool = new Mock<IMatchResultPool>();
            _mockOptions = new Mock<IOptionContext>();
            _engine = new ParallelGrepEngine(
                _mockStrategyFactory.Object,
                _mockFileSystem.Object,
                _mockPath.Object,
                _mockFileSearchService.Object,
                _mockPerformanceOptimizer.Object,
                _mockMatchResultPool.Object);
        }

        [Fact]
        public async Task SearchAsync_WithMultipleIncludeOptions_IncludesAllPatterns()
        {
            // Arrange
            var files = new[] { "test.cs", "test.js", "test.txt", "test.log" };
            var searchPattern = "test";
            
            // 複数の--includeオプションを模擬
            var includePatterns = new[] { "*.cs", "*.js" };
            
            SetupMockFileSystem(files);
            SetupMockOptions(searchPattern, includePatterns: includePatterns);
            
            // Act
            var result = await _engine.SearchAsync(_mockOptions.Object);
            
            // Assert
            Assert.Equal(2, result.TotalFiles); // test.cs と test.js のみ
            Assert.All(result.FileResults, fr => 
                Assert.True(fr.FileName.EndsWith(".cs") || fr.FileName.EndsWith(".js")));
        }

        [Fact]
        public async Task SearchAsync_WithMultipleExcludeOptions_ExcludesAllPatterns()
        {
            // Arrange
            var files = new[] { "test.cs", "test.js", "test.txt", "test.log" };
            var searchPattern = "test";
            
            // 複数の--excludeオプションを模擬
            var excludePatterns = new[] { "*.log", "*.txt" };
            
            SetupMockFileSystem(files);
            SetupMockOptions(searchPattern, excludePatterns: excludePatterns);
            
            // Act
            var result = await _engine.SearchAsync(_mockOptions.Object);
            
            // Assert
            Assert.Equal(2, result.TotalFiles); // test.cs と test.js のみ
            Assert.All(result.FileResults, fr => 
                Assert.True(fr.FileName.EndsWith(".cs") || fr.FileName.EndsWith(".js")));
        }

        [Fact]
        public async Task SearchAsync_WithMixedIncludeAndExclude_AppliesBothFilters()
        {
            // Arrange
            var files = new[] { "src/test.cs", "src/test.js", "tests/test.cs", "tests/test.js" };
            var searchPattern = "test";
            
            // 複数パターンのテスト
            var includePatterns = new[] { "*.cs", "*.js" };
            var excludePatterns = new[] { "tests/*" };
            
            SetupMockFileSystem(files);
            SetupMockOptions(searchPattern, includePatterns: includePatterns, excludePatterns: excludePatterns);
            
            // Act
            var result = await _engine.SearchAsync(_mockOptions.Object);
            
            // Assert
            Assert.Equal(2, result.TotalFiles); // src/test.cs と src/test.js のみ
            Assert.All(result.FileResults, fr => 
                Assert.StartsWith("src/", fr.FileName));
        }

        private void SetupMockFileSystem(string[] files)
        {
            _mockFileSystem.Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.SearchOption>()))
                .Returns(files);
            
            // 非同期版のEnumerateFilesAsyncもセットアップ
            _mockFileSystem.Setup(fs => fs.EnumerateFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.SearchOption>(), It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerable(files));
            
            foreach (var file in files)
            {
                _mockFileSystem.Setup(fs => fs.FileExists(file)).Returns(true);
                
                var mockFileInfo = new Mock<IFileInfo>();
                mockFileInfo.Setup(fi => fi.Length).Returns(100);
                _mockFileSystem.Setup(fs => fs.GetFileInfo(file)).Returns(mockFileInfo.Object);
                
                _mockFileSystem.Setup(fs => fs.OpenFile(file, It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()))
                    .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test content")));
                
                _mockPath.Setup(p => p.GetFileName(file)).Returns(Path.GetFileName(file));
                _mockPath.Setup(p => p.GetDirectoryName(file)).Returns(Path.GetDirectoryName(file));
            }

            _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);
        }
        
        // 配列をIAsyncEnumerableに変換するヘルパーメソッド
        private static async IAsyncEnumerable<string> ToAsyncEnumerable(string[] items)
        {
            foreach (var item in items)
            {
                await Task.Yield(); // 非同期コンテキストを維持
                yield return item;
            }
        }

        private void SetupMockOptions(string searchPattern, string[]? includePatterns = null, string[]? excludePatterns = null)
        {
            // 基本的なオプションのセットアップ
            _mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(searchPattern);
            _mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new ReadOnlyCollection<string>(new[] { "." }));
            _mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
            _mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
            _mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);

            // 複数パターンのセットアップ
            if (includePatterns != null)
            {
                _mockOptions.Setup(o => o.GetAllStringValues(OptionNames.IncludePattern))
                    .Returns(includePatterns.ToList().AsReadOnly());
            }
            else
            {
                _mockOptions.Setup(o => o.GetAllStringValues(OptionNames.IncludePattern))
                    .Returns(new List<string>().AsReadOnly());
            }

            if (excludePatterns != null)
            {
                _mockOptions.Setup(o => o.GetAllStringValues(OptionNames.ExcludePattern))
                    .Returns(excludePatterns.ToList().AsReadOnly());
            }
            else
            {
                _mockOptions.Setup(o => o.GetAllStringValues(OptionNames.ExcludePattern))
                    .Returns(new List<string>().AsReadOnly());
            }

            // ストラテジーのセットアップ
            var mockStrategy = new Mock<IMatchStrategy>();
            mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new[] { new MatchResult("test", 1, "test content", "test".AsMemory(), 0, 4) });
            _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
                .Returns(mockStrategy.Object);
        }
    }
}
