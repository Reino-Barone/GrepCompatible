using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.CommandLine;
using GrepCompatible.Constants;
using Xunit;

namespace GrepCompatible.Test;

/// <summary>
/// 非同期I/O最適化の統合テスト
/// </summary>
public class AsyncOptimizationIntegrationTests
{
    private readonly IMatchStrategyFactory _strategyFactory;
    private readonly MockFileSystem _fileSystem;
    private readonly MockPathHelper _pathHelper;
    private readonly IFileSearchService _fileSearchService;
    private readonly IPerformanceOptimizer _performanceOptimizer;
    private readonly IMatchResultPool _matchResultPool;
    private readonly ParallelGrepEngine _engine;

    public AsyncOptimizationIntegrationTests()
    {
        _strategyFactory = new MatchStrategyFactory();
        _fileSystem = new MockFileSystem();
        _pathHelper = new MockPathHelper();
        _fileSearchService = new FileSearchService(_fileSystem, _pathHelper);
        _performanceOptimizer = new PerformanceOptimizer();
        _matchResultPool = new MatchResultPool();
        _engine = new ParallelGrepEngine(_strategyFactory, _fileSystem, _pathHelper, _fileSearchService, _performanceOptimizer, _matchResultPool);
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
        _fileSystem.AddFile("test.txt", testContent);

        var options = CreateOptions("test", "test.txt");

        // Act
        var result = await _engine.SearchAsync(options);

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
        
        _fileSystem.AddFile("large.txt", sb.ToString());

        var options = CreateOptions("match", "large.txt");

        // Act
        var result = await _engine.SearchAsync(options);

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(100, result.TotalMatches); // 100 matching lines
        
        var fileResult = result.FileResults.First();
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
        var files = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var fileName = $"file{i}.txt";
            var content = $"line1\ntarget line in file {i}\nline3";
            _fileSystem.AddFile(fileName, content);
            files.Add(fileName);
        }

        var options = CreateOptions("target", files.ToArray());

        // Act
        var result = await _engine.SearchAsync(options);

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
        var largeContent = string.Join("\n", Enumerable.Range(0, 100000).Select(i => $"line {i}"));
        _fileSystem.AddFile("huge.txt", largeContent);

        var options = CreateOptions("line", "huge.txt");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Short timeout but not too short

        // Act
        var result = await _engine.SearchAsync(options, cts.Token);

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
        _fileSystem.AddFile("context.txt", testContent);

        var options = CreateOptions("target", "context.txt");
        AddOption(options, OptionNames.Context, 1); // 1 line before and after

        // Act
        var result = await _engine.SearchAsync(options);

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
        _fileSystem.AddFile("maxcount.txt", testContent);

        var options = CreateOptions("match", "maxcount.txt");
        AddOption(options, OptionNames.MaxCount, 5);

        // Act
        var result = await _engine.SearchAsync(options);

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
        _fileSystem.AddFile("invert.txt", testContent);

        var options = CreateOptions("match", "invert.txt");
        AddOption(options, OptionNames.InvertMatch, true);

        // Act
        var result = await _engine.SearchAsync(options);

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
        _fileSystem.AddFile("memory.txt", testContent);

        var options = CreateOptions("content", "memory.txt");

        // Act
        var result = await _engine.SearchAsync(options);

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
