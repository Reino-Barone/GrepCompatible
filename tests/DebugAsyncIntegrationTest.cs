using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Core;
using GrepCompatible.Strategies;
using GrepCompatible.Models;
using GrepCompatible.CommandLine;
using GrepCompatible.Constants;
using GrepCompatible.Abstractions;
using Moq;
using Xunit;

namespace GrepCompatible.Test;

public class DebugAsyncIntegrationTest
{
    private static void SetupPathHelper(Mock<IPath> pathHelper)
    {
        pathHelper.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetDirectoryName(path));
        pathHelper.Setup(p => p.GetFileName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFileName(path));
        pathHelper.Setup(p => p.Combine(It.IsAny<string[]>()))
            .Returns<string[]>(paths => Path.Combine(paths));
    }

    [Fact]
    public async Task Debug_SimpleSearch_ShouldWork()
    {
        // Arrange
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("test.txt", "line1\ntest line\nline3")
            .Build();

        var strategyFactory = new MatchStrategyFactory();
        var pathHelper = new Mock<IPath>();
        SetupPathHelper(pathHelper);
        var performanceOptimizer = new PerformanceOptimizer();
        var matchResultPool = new MatchResultPool();
        var fileSearchService = new FileSearchService(fileSystem, pathHelper.Object);
        
        var engine = new ParallelGrepEngine(strategyFactory, fileSystem, pathHelper.Object, fileSearchService, performanceOptimizer, matchResultPool);

        var options = new DynamicOptions();
        
        // Set up pattern argument
        var patternArg = new StringArgument(ArgumentNames.Pattern, "test");
        patternArg.TryParse("test");
        options.AddArgument(patternArg);
        
        // Set up files argument
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse("test.txt");
        options.AddArgument(filesArg);

        // Act
        var result = await engine.SearchAsync(options);

        // Debug output
        Console.WriteLine($"IsOverallSuccess: {result.IsOverallSuccess}");
        Console.WriteLine($"TotalFiles: {result.TotalFiles}");
        Console.WriteLine($"TotalMatches: {result.TotalMatches}");
        Console.WriteLine($"FileResults count: {result.FileResults.Count()}");
        
        foreach (var fileResult in result.FileResults)
        {
            Console.WriteLine($"  File: {fileResult.FileName}");
            Console.WriteLine($"  HasError: {fileResult.HasError}");
            Console.WriteLine($"  TotalMatches: {fileResult.TotalMatches}");
            if (fileResult.HasError)
            {
                Console.WriteLine($"  Error: {fileResult.ErrorMessage}");
            }
        }

        // Assert
        Assert.True(result.IsOverallSuccess);
        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.TotalMatches);
    }
}
