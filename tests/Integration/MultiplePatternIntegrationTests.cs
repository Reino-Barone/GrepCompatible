using GrepCompatible.Core;
using GrepCompatible.Abstractions.Constants;
using GrepCompatible.Abstractions;
using GrepCompatible.Core.Strategies;
using GrepCompatible.Test.Infrastructure;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace GrepCompatible.Test.Integration
{
    /// <summary>
    /// 複数パターン検索の統合テスト
    /// </summary>
    public class MultiplePatternIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public MultiplePatternIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private ParallelGrepEngine CreateEngine(IFileSystem fileSystem)
        {
            var strategyFactory = new MatchStrategyFactory();
            
            // IPathのモック設定を追加
            var mockPath = new Mock<IPath>();
            mockPath.Setup(p => p.GetFileName(It.IsAny<string>()))
                .Returns((string path) => System.IO.Path.GetFileName(path));
            mockPath.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
                .Returns((string path) => System.IO.Path.GetDirectoryName(path) ?? ".");
            
            var realFileSearchService = new FileSearchService(fileSystem, mockPath.Object);
            var performanceOptimizer = new PerformanceOptimizer();
            var matchResultPool = new MatchResultPool();

            return new ParallelGrepEngine(
                strategyFactory,
                fileSystem,
                mockPath.Object,
                realFileSearchService,
                performanceOptimizer,
                matchResultPool);
        }

        private IOptionContext CreateOptions(string pattern, string[]? includePatterns = null, string[]? excludePatterns = null)
        {
            var mockOptions = new Mock<IOptionContext>();
            
            mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(pattern);
            mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
                .Returns(new ReadOnlyCollection<string>(new[] { "." }));
            mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
            mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
            mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);

            if (includePatterns != null)
            {
                mockOptions.Setup(o => o.GetAllStringValues(OptionNames.IncludePattern))
                    .Returns(includePatterns.ToList().AsReadOnly());
            }
            else
            {
                mockOptions.Setup(o => o.GetAllStringValues(OptionNames.IncludePattern))
                    .Returns(new List<string>().AsReadOnly());
            }

            if (excludePatterns != null)
            {
                mockOptions.Setup(o => o.GetAllStringValues(OptionNames.ExcludePattern))
                    .Returns(excludePatterns.ToList().AsReadOnly());
            }
            else
            {
                mockOptions.Setup(o => o.GetAllStringValues(OptionNames.ExcludePattern))
                    .Returns(new List<string>().AsReadOnly());
            }

            return mockOptions.Object;
        }

                [Fact]
        public async Task SearchAsync_WithMultipleIncludeOptions_IncludesAllPatterns()
        {
            _output.WriteLine("開始: SearchAsync_WithMultipleIncludeOptions_IncludesAllPatterns");
            
            // Arrange
            var includePatterns = new[] { "*.cs", "*.js" };
            
            _output.WriteLine($"Includeパターン: [{string.Join(", ", includePatterns)}]");
            
            var fileSystem = FileSystemTestBuilder.CreateMultiplePatternTestFiles().Build();

            _output.WriteLine("ファイルシステムビルド完了");

            var engine = CreateEngine(fileSystem);
            var options = CreateOptions("test", includePatterns: includePatterns);

            _output.WriteLine("エンジンとオプション作成完了");
            
            // Act
            _output.WriteLine("SearchAsync開始");
            var result = await engine.SearchAsync(options);
            _output.WriteLine("SearchAsync完了");
            
            // Assert
            _output.WriteLine($"結果: TotalFiles={result.TotalFiles}, TotalMatches={result.TotalMatches}");
            _output.WriteLine($"FileResults count: {result.FileResults?.Count() ?? 0}");
            
            if (result.FileResults?.Any() == true)
            {
                foreach (var fr in result.FileResults)
                {
                    _output.WriteLine($"ファイル結果: {fr.FileName}, マッチ数={fr.TotalMatches}, エラー={fr.HasError}");
                    if (fr.HasError)
                    {
                        _output.WriteLine($"エラーメッセージ: {fr.ErrorMessage}");
                    }
                }
            }
            else
            {
                _output.WriteLine("ファイル結果が空またはnull");
            }
            
            Assert.Equal(2, result.TotalFiles); // test.cs と test.js のみ
            Assert.NotNull(result.FileResults);
            Assert.All(result.FileResults, fr => 
                Assert.True(fr.FileName.EndsWith(".cs") || fr.FileName.EndsWith(".js")));
        }

        [Fact]
        public async Task SearchAsync_WithMultipleExcludeOptions_ExcludesAllPatterns()
        {
            _output.WriteLine("開始: SearchAsync_WithMultipleExcludeOptions_ExcludesAllPatterns");
            
            // Arrange
            var excludePatterns = new[] { "*.log", "*.txt" };
            
            _output.WriteLine($"Excludeパターン: [{string.Join(", ", excludePatterns)}]");
            
            var fileSystem = FileSystemTestBuilder.CreateMultiplePatternTestFiles().Build();

            var engine = CreateEngine(fileSystem);
            var options = CreateOptions("test", excludePatterns: excludePatterns);
            
            // Act
            var result = await engine.SearchAsync(options);
            
            // Assert
            _output.WriteLine($"結果: TotalFiles={result.TotalFiles}, TotalMatches={result.TotalMatches}");
            Assert.Equal(2, result.TotalFiles); // test.cs と test.js のみ
            Assert.NotNull(result.FileResults);
            Assert.All(result.FileResults, fr => 
                Assert.True(fr.FileName.EndsWith(".cs") || fr.FileName.EndsWith(".js")));
        }

        [Fact]
        public async Task SearchAsync_WithMixedIncludeAndExclude_AppliesBothFilters()
        {
            _output.WriteLine("開始: SearchAsync_WithMixedIncludeAndExclude_AppliesBothFilters");
            
            // Arrange
            var includePatterns = new[] { "*.cs", "*.js" };
            var excludePatterns = new[] { "tests/*" };
            
            _output.WriteLine($"Includeパターン: [{string.Join(", ", includePatterns)}]");
            _output.WriteLine($"Excludeパターン: [{string.Join(", ", excludePatterns)}]");
            
            var fileSystem = FileSystemTestBuilder.CreateNestedMultiplePatternTestFiles().Build();

            var engine = CreateEngine(fileSystem);
            var options = CreateOptions("test", includePatterns: includePatterns, excludePatterns: excludePatterns);
            
            // Act
            var result = await engine.SearchAsync(options);
            
            // Assert
            _output.WriteLine($"結果: TotalFiles={result.TotalFiles}, TotalMatches={result.TotalMatches}");
            Assert.Equal(2, result.TotalFiles); // src/test.cs と src/test.js のみ
            Assert.NotNull(result.FileResults);
            Assert.All(result.FileResults, fr => 
                Assert.StartsWith("src/", fr.FileName));
        }
    }
}
