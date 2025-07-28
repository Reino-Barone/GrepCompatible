using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Core.Strategies;
using GrepCompatible.Test.Infrastructure;
using GrepCompatible.Abstractions.Constants;
using GrepCompatible.Abstractions.CommandLine;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace GrepCompatible.Test.Integration;

/// <summary>
/// MockFileSystemを使用したクロスプラットフォーム対応テスト例
/// </summary>
public class MockFileSystemIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly Mock<IFileSystem> _mockFileSystem = new();
    private readonly Mock<IPath> _mockPathHelper = new();
    private readonly Mock<IFileSearchService> _mockFileSearchService = new();
    private readonly Mock<IPerformanceOptimizer> _mockPerformanceOptimizer = new();
    private readonly IMatchResultPool _matchResultPool = new MatchResultPool(); // 実際の実装を使用
    private readonly ParallelGrepEngine _engine;

    public MockFileSystemIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        SetupPathHelper();
        SetupMockServices();
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        _engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            _mockFileSystem.Object,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _matchResultPool);
    }

    private void SetupMockServices()
    {
        // Performance Optimizer のセットアップ
        _mockPerformanceOptimizer.Setup(po => po.CalculateOptimalParallelism(It.IsAny<int>()))
            .Returns<int>(fileCount => Math.Max(1, Math.Min(Environment.ProcessorCount, fileCount == 0 ? 1 : fileCount)));
        
        _mockPerformanceOptimizer.Setup(po => po.GetOptimalBufferSize(It.IsAny<long>()))
            .Returns(4096);

        // FileSearchService のセットアップ
        _mockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                return Task.FromResult(files.AsEnumerable());
            });


    }

    private void SetupPathHelper()
    {
        _mockPathHelper.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetDirectoryName(path));
        _mockPathHelper.Setup(p => p.GetFileName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFileName(path));
        _mockPathHelper.Setup(p => p.Combine(It.IsAny<string[]>()))
            .Returns<string[]>(paths => Path.Combine(paths));
    }

    [Fact]
    public async Task SearchAsync_WithMockFileSystem_ShouldWorkAcrossAllPlatforms()
    {
        _output.WriteLine("開始: SearchAsync_WithMockFileSystem_ShouldWorkAcrossAllPlatforms");
        
        // Arrange - FileSystemTestBuilderを使用したインターフェース経由の設定
        var testFile = "src/test.txt";
        var testContent = "This is a test file\nwith multiple lines";
        
        _output.WriteLine($"テストファイル: {testFile}");
        _output.WriteLine($"テストコンテンツ: {testContent}");
        
        // FileSystemTestBuilderを使用してファイルシステムモックを構築
        var fileSystemBuilder = new FileSystemTestBuilder();
        var fileSystem = fileSystemBuilder
            .WithFile(testFile, testContent)
            .WithFiles(testFile)
            .Build();
        
        _output.WriteLine("FileSystem構築完了");
        
        // エンジンを再構築（新しいファイルシステムで）
        var engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            fileSystem,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _matchResultPool);
        
        var matches = new List<MatchResult>
        {
            new(testFile, 1, "This is a test file", "test".AsMemory(), 10, 14)
        };
        
        _mockStrategy.Setup(s => s.FindMatches(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<IOptionContext>(), 
            It.IsAny<string>(), 
            It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: {line}, Pattern: {pattern}, FileName: {fileName}, LineNumber: {lineNumber}");
                // ファイル名に応じて適切なマッチを返す
                if (fileName.Contains("test.txt") && line.Contains("test"))
                {
                    _output.WriteLine($"マッチを返します: {line}");
                    return new List<MatchResult> { new(fileName, lineNumber, line, "test".AsMemory(), line.IndexOf("test"), 14) };
                }
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });
        
        var options = new DynamicOptions();
        
        // パターン引数を設定
        var patternArg = new StringArgument(ArgumentNames.Pattern, "Search pattern", "", true);
        patternArg.TryParse("test");
        options.AddArgument(patternArg);
        
        // ファイル引数を設定
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse(testFile);
        options.AddArgument(filesArg);
        
        _output.WriteLine("オプション設定完了");
        
        // Act
        _output.WriteLine("SearchAsync開始");
        var result = await engine.SearchAsync(options);
        
        _output.WriteLine($"SearchAsync完了 - Results: {result?.FileResults?.Count ?? 0}");
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal(testFile, result.FileResults[0].FileName);
        Assert.Single(result.FileResults[0].Matches);
        Assert.Equal("test", result.FileResults[0].Matches[0].MatchedText.ToString());
        
        _output.WriteLine("テスト完了");
    }

    [Fact]
    public async Task FileSystemMock_PathHandling_ShouldSupportDifferentFormats()
    {
        // Arrange - インターフェース経由でのパス処理テスト
        var windowsStylePath = "src\\test.txt";
        var unixStylePath = "src/test.txt"; 
        var content = "test content line 1\ntest content line 2";
        
        // FileSystemTestBuilderを使って両方のパス形式をサポート
        var fileSystem = new FileSystemTestBuilder()
            .WithFile(windowsStylePath, content)
            .WithFile(unixStylePath, content)
            .Build();
        
        // Act & Assert - 両方の形式で読み取り可能
        var lines1 = new List<string>();
        await foreach (var line in fileSystem.ReadLinesAsync(windowsStylePath))
        {
            lines1.Add(line);
        }
        
        var lines2 = new List<string>();
        await foreach (var line in fileSystem.ReadLinesAsync(unixStylePath))
        {
            lines2.Add(line);
        }
        
        Assert.Equal(2, lines1.Count);
        Assert.Equal(2, lines2.Count);
        Assert.Equal("test content line 1", lines1[0]);
        Assert.Equal("test content line 1", lines2[0]);
    }

    [Fact]
    public async Task FileSystemMock_DirectoryListing_ShouldEnumerateFiles()
    {
        _output.WriteLine("開始: FileSystemMock_DirectoryListing_ShouldEnumerateFiles");
        
        // Arrange - FileSystemTestBuilderを使用したディレクトリ・ファイル構造の構築
        _output.WriteLine("FileSystemTestBuilderを作成中...");
        var fileSystem = new FileSystemTestBuilder()
            .WithDirectory("src")
            .WithFile("src/file1.txt", "content1")
            .WithFile("src/file2.txt", "content2")
            .WithFile("src/subdir/file3.txt", "content3")
            .WithFiles("src/file1.txt", "src/file2.txt", "src/subdir/file3.txt")
            .Build();
        
        _output.WriteLine($"FileSystem作成完了: {fileSystem?.GetType().Name ?? "null"}");
        
        if (fileSystem == null)
        {
            _output.WriteLine("エラー: fileSystemがnullです");
            throw new InvalidOperationException("FileSystemTestBuilderがnullのFileSystemを返しました");
        }
        
        // Act - 非同期ファイル列挙のテスト
        _output.WriteLine("ファイル列挙を開始...");
        var allFiles = new List<string>();
        
        try
        {
            await foreach (var file in fileSystem.EnumerateFilesAsync("src", "*", System.IO.SearchOption.AllDirectories))
            {
                _output.WriteLine($"ファイル発見: {file}");
                allFiles.Add(file);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ファイル列挙中にエラー: {ex.Message}");
            _output.WriteLine($"スタックトレース: {ex.StackTrace}");
            throw;
        }
        
        _output.WriteLine($"列挙されたファイル数: {allFiles.Count}");
        
        // Assert
        Assert.Equal(3, allFiles.Count);
        Assert.Contains("src/file1.txt", allFiles);
        Assert.Contains("src/file2.txt", allFiles);
        Assert.Contains("src/subdir/file3.txt", allFiles);
        
        _output.WriteLine("テスト完了");
    }

    [Fact]
    public async Task SearchAsync_WithStandardInput_ShouldWorkCorrectly()
    {
        _output.WriteLine("開始: SearchAsync_WithStandardInput_ShouldWorkCorrectly");
        
        // Arrange - FileSystemTestBuilderを使用した標準入力設定
        var standardInputContent = "line1 with test\nline2 without match\nline3 with test again";
        _output.WriteLine($"標準入力コンテンツ: {standardInputContent}");
        
        var fileSystem = new FileSystemTestBuilder()
            .WithStandardInput(standardInputContent)
            .Build();
        
        _output.WriteLine("FileSystem構築完了");
        
        // エンジンを標準入力対応で再構築
        var engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            fileSystem,
            _mockPathHelper.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _matchResultPool);
        
        var matches = new List<MatchResult>
        {
            new("(standard input)", 1, "line1 with test", "test".AsMemory(), 11, 15),
            new("(standard input)", 3, "line3 with test again", "test".AsMemory(), 11, 15)
        };
        
        _mockStrategy.Setup(s => s.FindMatches(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<IOptionContext>(), 
            It.IsAny<string>(), 
            It.IsAny<int>()))
            .Returns((string line, string pattern, IOptionContext options, string fileName, int lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: {line}, Pattern: {pattern}, FileName: {fileName}, LineNumber: {lineNumber}");
                if (line.Contains("test"))
                {
                    var match = matches.FirstOrDefault(m => m.LineNumber == lineNumber);
                    if (match != null)
                    {
                        _output.WriteLine($"マッチを返します: {match.Line}");
                        return new List<MatchResult> { match };
                    }
                }
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });
        
        var options = new DynamicOptions();
        
        // パターン引数を設定
        var patternArg = new StringArgument(ArgumentNames.Pattern, "Search pattern", "", true);
        patternArg.TryParse("test");
        options.AddArgument(patternArg);
        
        // ファイル引数を設定（標準入力）
        var filesArg = new StringListArgument(ArgumentNames.Files, "Files to search", false);
        filesArg.TryParse("-"); // 標準入力を指定
        options.AddArgument(filesArg);
        
        _output.WriteLine("オプション設定完了");
        
        // Act
        _output.WriteLine("SearchAsync開始");
        var result = await engine.SearchAsync(options);
        
        _output.WriteLine($"SearchAsync完了 - Results: {result?.FileResults?.Count ?? 0}");
        
        // Assert
        Assert.NotNull(result);
        Assert.Single(result.FileResults);
        Assert.Equal("(standard input)", result.FileResults[0].FileName);
        Assert.Equal(2, result.FileResults[0].TotalMatches);
        
        _output.WriteLine("テスト完了");
    }
}
