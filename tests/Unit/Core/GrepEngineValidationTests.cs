using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Core.Strategies;
using GrepCompatible.Abstractions.Constants;
using Moq;
using System;
using Xunit;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// GrepEngineコンストラクタ・バリデーションのテスト
/// </summary>
public class GrepEngineValidationTests : GrepEngineTestsBase
{
    [Fact]
    public void Constructor_WithNullStrategyFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ParallelGrepEngine(null!, MockFileSystem.Object, MockPathHelper.Object, 
                MockFileSearchService.Object, MockPerformanceOptimizer.Object, MockMatchResultPool.Object));
    }

    [Fact]
    public void Constructor_WithNullFileSystem_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ParallelGrepEngine(MockStrategyFactory.Object, null!, MockPathHelper.Object,
                MockFileSearchService.Object, MockPerformanceOptimizer.Object, MockMatchResultPool.Object));
    }

    [Fact]
    public void Constructor_WithNullPerformanceOptimizer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ParallelGrepEngine(MockStrategyFactory.Object, MockFileSystem.Object, MockPathHelper.Object,
                MockFileSearchService.Object, null!, MockMatchResultPool.Object));
    }
}
