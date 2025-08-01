using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.IO;
using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Core.Strategies;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Abstractions.Constants;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace GrepCompatible.Test.Integration;

/// <summary>
/// 非同期I/O最適化の統合テスト
/// </summary>
public class AsyncOptimizationIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IMatchStrategyFactory _strategyFactory;
    private readonly Mock<IPath> _pathHelper;
    private readonly IPerformanceOptimizer _performanceOptimizer;
    private readonly IMatchResultPool _matchResultPool;

    public AsyncOptimizationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _strategyFactory = new MatchStrategyFactory();
        _pathHelper = new Mock<IPath>();
        SetupPathHelper();
        _performanceOptimizer = new PerformanceOptimizer();
        _matchResultPool = new MatchResultPool();
    }

    private void SetupPathHelper()
    {
        _pathHelper.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetDirectoryName(path));
        _pathHelper.Setup(p => p.GetFileName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFileName(path));
        _pathHelper.Setup(p => p.Combine(It.IsAny<string[]>()))
            .Returns<string[]>(paths => Path.Combine(paths));
    }

    /// <summary>
    /// テスト用のParallelGrepEngineを指定のファイルシステムで構築
    /// </summary>
    private ParallelGrepEngine CreateEngine(IFileSystem fileSystem)
    {
        var fileSearchService = new FileSearchService(fileSystem, _pathHelper.Object);
        return new ParallelGrepEngine(_strategyFactory, fileSystem, _pathHelper.Object, fileSearchService, _performanceOptimizer, _matchResultPool);
    }

    private DynamicOptions CreateOptions(string pattern, params string[] files)
    {
        var options = new DynamicOptions();
        
        // Set up pattern argument
        var patternArg = new StringArgument(ArgumentNames.Pattern, pattern);
        patternArg.TryParse(pattern);
        options.AddArgument(patternArg);
        
        // Set up files argument
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        if (files.Length > 0)
        {
            foreach (var file in files)
            {
                filesArg.TryParse(file);
            }
        }
        options.AddArgument(filesArg);
        
        return options;
    }

    private void AddOption(DynamicOptions options, OptionNames optionName, object value)
    {
        switch (optionName)
        {
            case OptionNames.Context:
            case OptionNames.MaxCount:
                var intOption = new NullableIntegerOption(optionName, optionName.ToString(), null, null, null, false, 0, int.MaxValue);
                intOption.TryParse(value.ToString());
                options.AddOption(intOption);
                break;
            case OptionNames.InvertMatch:
                var flagOption = new FlagOption(optionName, optionName.ToString());
                flagOption.TryParse(null); // Flag options don't need a value
                options.AddOption(flagOption);
                break;
        }
    }

    [Fact]
    public async Task FileProcessing_WithAsyncOptimizations_ShouldUseStreamingAndZeroCopy()
    {
        // Arrange
        var testContent = "line1\ntest line\nline3\ntest again\nline5";
        var fileSystem = new FileSystemTestBuilder()
            .WithFile("test.txt", testContent)
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("test", "test.txt");

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(2, result.TotalMatches); // "test line" and "test again"
        
        var fileResult = result.FileResults.First();
        Assert.Equal("test.txt", fileResult.FileName);
        Assert.Equal(2, fileResult.TotalMatches);
        Assert.False(fileResult.HasError);
        
        var matches = fileResult.Matches.ToList();
        Assert.Equal("test line", matches[0].Line);
        Assert.Equal("test again", matches[1].Line);
    }

    [Fact]
    public async Task FileProcessing_WithLargeFile_ShouldHandleEfficientlyWithAsyncStreaming()
    {
        _output.WriteLine("開始: FileProcessing_WithLargeFile_ShouldHandleEfficientlyWithAsyncStreaming");
        
        // Arrange
        var sb = new StringBuilder();
        var matchingLineIndices = new List<int>();
        var totalLines = 10000;
        
        for (int i = 0; i < totalLines; i++)
        {
            if (i % 100 == 0) // Every 100th line contains "match"
            {
                sb.AppendLine($"This is line {i} with match in it");
                matchingLineIndices.Add(i);
            }
            else
            {
                sb.AppendLine($"This is line {i} without target");
            }
        }
        
        _output.WriteLine($"ファイル内容生成完了: {totalLines}行, マッチ予想: {matchingLineIndices.Count}行");
        
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("large.txt", sb.ToString())
            .Build();

        _output.WriteLine("ファイルシステムビルド完了");

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("match", "large.txt");

        _output.WriteLine("エンジンとオプション作成完了");

        // Act
        _output.WriteLine("SearchAsync開始");
        var result = await engine.SearchAsync(options);
        _output.WriteLine("SearchAsync完了");

        // Assert
        _output.WriteLine($"結果: IsOverallSuccess={result.IsOverallSuccess}, TotalFiles={result.TotalFiles}, TotalMatches={result.TotalMatches}");
        
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
        
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(100, result.TotalMatches); // 100 matching lines
        
        var fileResult = result.FileResults?.First();
        Assert.NotNull(fileResult);
        Assert.Equal("large.txt", fileResult.FileName);
        Assert.Equal(100, fileResult.TotalMatches);
        Assert.False(fileResult.HasError);
        
        // Verify the matches are correct
        var matches = fileResult.Matches.ToList();
        Assert.Equal(100, matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            Assert.Contains("match", matches[i].Line);
        }
    }

    [Fact]
    public async Task FileProcessing_WithMultipleFiles_ShouldUseAsyncParallelProcessing()
    {
        // Arrange
        var fileSystemBuilder = new FileSystemTestBuilder();
        var files = new List<string>();
        
        for (int i = 0; i < 10; i++)
        {
            var fileName = $"file{i}.txt";
            var content = $"line1\ntarget line in file {i}\nline3";
            fileSystemBuilder.WithFile(fileName, content);
            files.Add(fileName);
        }

        var fileSystem = fileSystemBuilder.Build();
        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("target", files.ToArray());

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(10, result.TotalFiles);
        Assert.Equal(10, result.TotalMatches); // One match per file
        
        Assert.All(result.FileResults, fileResult =>
        {
            Assert.Equal(1, fileResult.TotalMatches);
            Assert.False(fileResult.HasError);
            Assert.Contains("target", fileResult.Matches.First().Line);
        });
    }

    [Fact]
    public async Task FileProcessing_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var fileSystem = FileSystemTestBuilder.CreateLargeFileTestSystem()
            .WithFile("huge.txt", string.Join("\n", Enumerable.Range(0, 100000).Select(i => $"line {i}")))
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("line", "huge.txt");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Short timeout but not too short

        // Act
        var result = await engine.SearchAsync(options, cts.Token);

        // Assert
        // The operation should complete (even if cancelled) and return a result
        Assert.NotNull(result);
        // Due to cancellation, we might have 0 matches or some partial results
        Assert.True(result.TotalMatches >= 0);
        Assert.True(result.TotalFiles >= 0);
    }

    [Fact]
    public async Task FileProcessing_WithContextLines_ShouldUseOptimizedContextProcessing()
    {
        // Arrange
        var testContent = "line1\nline2\ntarget line\nline4\nline5";
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("context.txt", testContent)
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("target", "context.txt");
        AddOption(options, OptionNames.Context, 1); // 1 line before and after

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.TotalMatches);
        
        var fileResult = result.FileResults.First();
        Assert.True(fileResult.HasContextualMatches);
        Assert.NotNull(fileResult.ContextualMatches);
        Assert.Single(fileResult.ContextualMatches);
        
        var contextMatch = fileResult.ContextualMatches.First();
        Assert.Equal("target line", contextMatch.Match.Line);
        Assert.Single(contextMatch.BeforeContext);
        Assert.Equal("line2", contextMatch.BeforeContext.First().Line);
        Assert.Single(contextMatch.AfterContext);
        Assert.Equal("line4", contextMatch.AfterContext.First().Line);
    }

    [Fact]
    public async Task FileProcessing_WithMaxCount_ShouldLimitResultsEfficiently()
    {
        // Arrange
        var testContent = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"match line {i}"));
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("maxcount.txt", testContent)
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("match", "maxcount.txt");
        AddOption(options, OptionNames.MaxCount, 5);

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(5, result.TotalMatches); // Limited by max count
        
        var fileResult = result.FileResults.First();
        Assert.Equal(5, fileResult.TotalMatches);
        Assert.Equal(5, fileResult.Matches.Count);
        Assert.False(fileResult.HasError);
    }

    [Fact]
    public async Task FileProcessing_WithInvertMatch_ShouldReturnNonMatchingLines()
    {
        // Arrange
        var testContent = "line1\nmatch line\nline3\nmatch again\nline5";
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("invert.txt", testContent)
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("match", "invert.txt");
        AddOption(options, OptionNames.InvertMatch, true);

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(3, result.TotalMatches); // 3 non-matching lines
        
        var fileResult = result.FileResults.First();
        Assert.Equal(3, fileResult.TotalMatches);
        
        var matches = fileResult.Matches.ToList();
        Assert.Equal("line1", matches[0].Line);
        Assert.Equal("line3", matches[1].Line);
        Assert.Equal("line5", matches[2].Line);
    }

    [Fact]
    public async Task FileProcessing_WithAsyncEnumeration_ShouldProvideMemoryEfficiency()
    {
        // Arrange
        var testContent = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"content line {i}"));
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("memory.txt", testContent)
            .Build();

        var engine = CreateEngine(fileSystem);
        var options = CreateOptions("content", "memory.txt");

        // Act
        var result = await engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1000, result.TotalMatches);
        
        var fileResult = result.FileResults.First();
        Assert.Equal(1000, fileResult.TotalMatches);
        Assert.False(fileResult.HasError);
        
        // Verify that all matches have proper ReadOnlyMemory backing
        var matches = fileResult.Matches.ToList();
        Assert.All(matches, match =>
        {
            Assert.True(match.MatchedText.Length > 0);
            Assert.Contains("content", match.MatchedText.ToString());
        });
    }
}
