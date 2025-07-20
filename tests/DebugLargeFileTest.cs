using System.Text;
using System.Linq;
using System.Threading.Tasks;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Core;
using GrepCompatible.Strategies;
using GrepCompatible.Models;
using GrepCompatible.CommandLine;
using GrepCompatible.Constants;
using Xunit;

namespace GrepCompatible.Test;

public class DebugLargeFileTest
{
    [Fact]
    public async Task Debug_LargeFile_ShouldWork()
    {
        // Arrange - 小さなバージョンで開始
        var sb = new StringBuilder();
        var matchingLineIndices = new List<int>();
        var totalLines = 200; // Original is 10000, debugging with smaller number

        for (int i = 0; i < totalLines; i++)
        {
            if (i % 20 == 0) // Every 20th line contains "match" - should be 10 matches
            {
                sb.AppendLine($"This is line {i} with match in it");
                matchingLineIndices.Add(i);
            }
            else
            {
                sb.AppendLine($"This is line {i} without target");
            }
        }

        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile("large.txt", sb.ToString())
            .Build();

        var strategyFactory = new MatchStrategyFactory();
        var pathHelper = new MockPathHelper();
        var performanceOptimizer = new PerformanceOptimizer();
        var matchResultPool = new MatchResultPool();
        var fileSearchService = new FileSearchService(fileSystem, pathHelper);
        
        var engine = new ParallelGrepEngine(strategyFactory, fileSystem, pathHelper, fileSearchService, performanceOptimizer, matchResultPool);

        var options = new DynamicOptions();
        
        // Set up pattern argument
        var patternArg = new StringArgument(ArgumentNames.Pattern, "match");
        patternArg.TryParse("match");
        options.AddArgument(patternArg);
        
        // Set up files argument
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse("large.txt");
        options.AddArgument(filesArg);

        // Act
        try
        {
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
                else
                {
                    Console.WriteLine($"  Match count: {fileResult.Matches.Count()}");
                    foreach (var match in fileResult.Matches.Take(3)) // Show first 3 matches
                    {
                        Console.WriteLine($"    Line {match.LineNumber}: {match.Line}");
                    }
                }
            }

            Console.WriteLine($"Expected matches: {matchingLineIndices.Count}");
            
            // Assert
            Assert.True(result.IsOverallSuccess);
            Assert.Equal(1, result.TotalFiles);
            Assert.Equal(10, result.TotalMatches); // Expected matches
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during execution: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
