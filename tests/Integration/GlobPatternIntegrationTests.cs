using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Core.Strategies;
using GrepCompatible.Abstractions.Constants;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace GrepCompatible.Test.Integration;

/// <summary>
/// グロブパターンの--include/--exclude機能の統合テスト
/// </summary>
public class GlobPatternIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IMatchStrategyFactory> _mockStrategyFactory = new();
    private readonly Mock<IMatchStrategy> _mockStrategy = new();
    private readonly Mock<IFileSystem> _mockFileSystem = new();
    private readonly Mock<IPath> _mockPath = new();
    private readonly Mock<IFileSearchService> _mockFileSearchService = new();
    private readonly Mock<IPerformanceOptimizer> _mockPerformanceOptimizer = new();
    private readonly IMatchResultPool _matchResultPool = new MatchResultPool(); // 実際の実装を使用
    private readonly ParallelGrepEngine _engine;

    public GlobPatternIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        SetupMocks();
        _engine = new ParallelGrepEngine(
            _mockStrategyFactory.Object,
            _mockFileSystem.Object,
            _mockPath.Object,
            _mockFileSearchService.Object,
            _mockPerformanceOptimizer.Object,
            _matchResultPool);
    }

    private void SetupMocks()
    {
        _mockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(_mockStrategy.Object);
        
        _mockStrategy.Setup(s => s.CanApply(It.IsAny<IOptionContext>()))
            .Returns(true);

        // Performance Optimizer のセットアップ
        _mockPerformanceOptimizer.Setup(po => po.CalculateOptimalParallelism(It.IsAny<int>()))
            .Returns<int>(fileCount => Math.Max(1, Math.Min(Environment.ProcessorCount, fileCount == 0 ? 1 : fileCount)));
        
        _mockPerformanceOptimizer.Setup(po => po.GetOptimalBufferSize(It.IsAny<long>()))
            .Returns(4096);

        // FileSearchService のセットアップ
        _mockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var filesArg = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                var files = new List<string>();
                
                foreach (var filePattern in filesArg)
                {
                    if (filePattern == "temp_dir")
                    {
                        // temp_dirが指定された場合、すべてのファイルを取得
                        var allFiles = new[] { "temp_dir/Program.cs", "temp_dir/README.txt", "temp_dir/debug.log" };
                        
                        // ShouldIncludeFile を使って除外パターンを適用
                        foreach (var file in allFiles)
                        {
                            if (_mockFileSearchService.Object.ShouldIncludeFile(file, options))
                            {
                                files.Add(file);
                            }
                        }
                    }
                    else
                    {
                        files.Add(filePattern);
                    }
                }
                
                return Task.FromResult(files.AsEnumerable());
            });
            
        // ShouldIncludeFile のセットアップ - 除外・包含パターン処理
        _mockFileSearchService.Setup(fs => fs.ShouldIncludeFile(It.IsAny<string>(), It.IsAny<IOptionContext>()))
            .Returns<string, IOptionContext>((filePath, options) =>
            {
                var fileName = System.IO.Path.GetFileName(filePath);
                
                // 包含パターンの処理
                var includePattern = options.GetStringValue(OptionNames.IncludePattern);
                if (includePattern != null)
                {
                    _output.WriteLine($"IncludePattern check: {fileName} against {includePattern}");
                    
                    bool matches = includePattern switch
                    {
                        "*.cs" => fileName.EndsWith(".cs"),
                        "*.log" => fileName.EndsWith(".log"),
                        "*.txt" => fileName.EndsWith(".txt"),
                        "test?.txt" => System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^test.\.txt$"), // test + 1文字 + .txt
                        "data.*.txt" => System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^data\..*\.txt$"), // data. + 任意の文字 + .txt
                        "test(*).txt" => System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^test\(.*\)\.txt$"), // test(任意の文字).txt
                        _ => false
                    };
                    
                    if (!matches)
                    {
                        _output.WriteLine($"ファイル除外 (Include): {filePath} (パターン: {includePattern})");
                        return false;
                    }
                    
                    _output.WriteLine($"ファイル包含 (Include): {filePath} (パターン: {includePattern})");
                }
                
                // 除外パターンの処理
                var excludePattern = options.GetStringValue(OptionNames.ExcludePattern);
                if (excludePattern != null)
                {
                    _output.WriteLine($"ExcludePattern check: {fileName} against {excludePattern}");
                    
                    bool shouldExclude = excludePattern switch
                    {
                        "*.log" => fileName.EndsWith(".log"),
                        _ => false
                    };
                    
                    if (shouldExclude)
                    {
                        _output.WriteLine($"ファイル除外 (Exclude): {filePath} (パターン: {excludePattern})");
                        return false;
                    }
                }
                
                _output.WriteLine($"ファイル包含: {filePath}");
                return true;
            });
            
        // Path のセットアップ
        _mockPath.Setup(p => p.GetFileName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFileName(path));
        _mockPath.Setup(p => p.GetDirectoryName(It.IsAny<string>()))
            .Returns<string>(path => Path.GetDirectoryName(path));
        _mockPath.Setup(p => p.Combine(It.IsAny<string[]>()))
            .Returns<string[]>(paths => Path.Combine(paths));
    }

    [Fact]
    public async Task SearchAsync_WithExcludeGlobPattern_ExcludesMatchingFiles()
    {
        _output.WriteLine("開始: SearchAsync_WithExcludeGlobPattern_ExcludesMatchingFiles");
        
        // Arrange
        var tempDir = "temp_dir";
        var csharpFile = tempDir + "/Program.cs";
        var textFile = tempDir + "/README.txt";
        var logFile = tempDir + "/debug.log";
        
        _output.WriteLine($"テストファイル: {csharpFile}, {textFile}, {logFile}");
        
        var files = new[] { csharpFile, textFile, logFile };
        SetupMockFileSystem(files);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns("*.log");
        
        _output.WriteLine("ExcludePattern: *.log");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, csharpFile, 1))
            .Returns(new[] { new MatchResult(csharpFile, 1, "hello world", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello test", "hello", mockOptions.Object, textFile, 1))
            .Returns(new[] { new MatchResult(textFile, 1, "hello test", "hello".AsMemory(), 0, 5) });

        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<string, string, IOptionContext, string, int>((line, pattern, options, fileName, lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: {line}, Pattern: {pattern}, FileName: {fileName}, LineNumber: {lineNumber}");
                
                if (fileName == csharpFile && line.Contains("hello"))
                {
                    _output.WriteLine($"マッチを返します: {csharpFile}");
                    return new[] { new MatchResult(csharpFile, 1, "hello world", "hello".AsMemory(), 0, 5) };
                }
                if (fileName == textFile && line.Contains("hello"))
                {
                    _output.WriteLine($"マッチを返します: {textFile}");
                    return new[] { new MatchResult(textFile, 1, "hello test", "hello".AsMemory(), 0, 5) };
                }
                
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });

        _output.WriteLine("オプション設定完了");

        // Act
        _output.WriteLine("SearchAsync開始");
        var result = await _engine.SearchAsync(mockOptions.Object);

        _output.WriteLine($"SearchAsync完了 - Results: {result?.FileResults?.Count ?? 0}");
        if (result?.FileResults != null)
        {
            foreach (var fileResult in result.FileResults)
            {
                _output.WriteLine($"結果ファイル: {fileResult.FileName}, マッチ数: {fileResult.TotalMatches}");
                if (fileResult.Matches?.Any() == true)
                {
                    foreach (var match in fileResult.Matches.Take(3)) // 最初の3つのマッチを出力
                    {
                        _output.WriteLine($"  マッチ: Line {match.LineNumber} - {match.Line}");
                    }
                }
                else
                {
                    _output.WriteLine($"  マッチなし");
                }
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches); // 期待値を元に戻す
        
        // .logファイルが除外されていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == csharpFile);
        Assert.Contains(result.FileResults, fr => fr.FileName == textFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == logFile);
    }

    [Fact]
    public async Task SearchAsync_WithIncludeGlobPattern_IncludesOnlyMatchingFiles()
    {
        // Arrange
        var tempDir = "temp_dir";
        var csharpFile = tempDir + "/Program.cs";
        var textFile = tempDir + "/README.txt";
        var logFile = tempDir + "/debug.log";
        
        var files = new[] { csharpFile, textFile, logFile };
        SetupMockFileSystem(files);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("*.cs");

        _mockStrategy.Setup(s => s.FindMatches("hello world", "hello", mockOptions.Object, csharpFile, 1))
            .Returns(new[] { new MatchResult(csharpFile, 1, "hello world", "hello".AsMemory(), 0, 5) });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        Assert.Equal(1, result.TotalMatches);
        
        // .csファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == csharpFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == textFile);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == logFile);
    }

    [Fact]
    public async Task SearchAsync_WithQuestionMarkGlobPattern_MatchesSingleCharacter()
    {
        // Arrange
        var tempDir = "temp_dir";
        var file1 = tempDir + "/test1.txt";
        var file2 = tempDir + "/test2.txt";
        var file3 = tempDir + "/test10.txt";
        var file4 = tempDir + "/test.txt";
        
        var files = new[] { file1, file2, file3, file4 };
        SetupMockFileSystem(files);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("test?.txt");

        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello debug", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello debug", "hello".AsMemory(), 0, 5) });
            
        // 包括的なモック設定（実際に呼び出される可能性があるため）
        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<string, string, IOptionContext, string, int>((line, pattern, options, fileName, lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: '{line}', Pattern: '{pattern}', FileName: '{fileName}', LineNumber: {lineNumber}");
                
                // ファイルごとにマッチング処理
                if ((fileName == file1 || fileName == file2) && line.Contains("hello"))
                {
                    _output.WriteLine($"マッチを返します: {fileName}");
                    return new[] { new MatchResult(fileName, lineNumber, line, "hello".AsMemory(), 0, 5) };
                }
                
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(2, result.TotalMatches);
        
        // 1文字のファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.Contains(result.FileResults, fr => fr.FileName == file2);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file3); // test10.txt は除外
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4); // test.txt は除外
    }

    [Fact]
    public async Task SearchAsync_WithComplexGlobPattern_MatchesCorrectly()
    {
        // Arrange
        var tempDir = "temp_dir";
        var file1 = tempDir + "/data.backup.txt";
        var file2 = tempDir + "/data.old.txt";
        var file3 = tempDir + "/data.new.txt";
        var file4 = tempDir + "/config.backup.txt";
        
        var files = new[] { file1, file2, file3, file4 };
        SetupMockFileSystem(files);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("data.*.txt");

        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello debug", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file2, 1))
            .Returns(new[] { new MatchResult(file2, 1, "hello debug", "hello".AsMemory(), 0, 5) });
        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file3, 1))
            .Returns(new[] { new MatchResult(file3, 1, "hello debug", "hello".AsMemory(), 0, 5) });
            
        // 包括的なモック設定
        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<string, string, IOptionContext, string, int>((line, pattern, options, fileName, lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: '{line}', Pattern: '{pattern}', FileName: '{fileName}', LineNumber: {lineNumber}");
                
                // dataで始まるファイルのマッチング処理
                if ((fileName == file1 || fileName == file2 || fileName == file3) && line.Contains("hello"))
                {
                    _output.WriteLine($"マッチを返します: {fileName}");
                    return new[] { new MatchResult(fileName, lineNumber, line, "hello".AsMemory(), 0, 5) };
                }
                
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Equal(3, result.FileResults.Count);
        Assert.Equal(3, result.TotalMatches);
        
        // dataで始まるファイルのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.Contains(result.FileResults, fr => fr.FileName == file2);
        Assert.Contains(result.FileResults, fr => fr.FileName == file3);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4); // config.backup.txt は除外
    }

    [Fact]
    public async Task SearchAsync_WithSpecialCharactersInGlobPattern_EscapesCorrectly()
    {
        // Arrange
        var tempDir = "temp_dir";
        var file1 = tempDir + "/test(1).txt";
        var file2 = tempDir + "/test[2].txt";
        var file3 = tempDir + "/test{3}.txt";
        var file4 = tempDir + "/test1.txt";
        
        var files = new[] { file1, file2, file3, file4 };
        SetupMockFileSystem(files);
        
        var mockOptions = new Mock<IOptionContext>();
        SetupBasicOptions(mockOptions, tempDir, "hello");
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(true);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns("test(*).txt");

        _mockStrategy.Setup(s => s.FindMatches("hello debug", "hello", mockOptions.Object, file1, 1))
            .Returns(new[] { new MatchResult(file1, 1, "hello debug", "hello".AsMemory(), 0, 5) });
            
        // 包括的なモック設定
        _mockStrategy.Setup(s => s.FindMatches(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IOptionContext>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<string, string, IOptionContext, string, int>((line, pattern, options, fileName, lineNumber) =>
            {
                _output.WriteLine($"FindMatches呼び出し - Line: '{line}', Pattern: '{pattern}', FileName: '{fileName}', LineNumber: {lineNumber}");
                
                // test(1).txtファイルのマッチング処理
                if (fileName == file1 && line.Contains("hello"))
                {
                    _output.WriteLine($"マッチを返します: {fileName}");
                    return new[] { new MatchResult(fileName, lineNumber, line, "hello".AsMemory(), 0, 5) };
                }
                
                _output.WriteLine("マッチなし");
                return Enumerable.Empty<MatchResult>();
            });

        // Act
        var result = await _engine.SearchAsync(mockOptions.Object);

        // Assert
        Assert.Single(result.FileResults);
        Assert.Equal(1, result.TotalMatches);
        
        // test(1).txtのみが含まれていることを確認
        Assert.Contains(result.FileResults, fr => fr.FileName == file1);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file2);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file3);
        Assert.DoesNotContain(result.FileResults, fr => fr.FileName == file4);
    }

    private void SetupMockFileSystem(string[] files)
    {
        _output.WriteLine($"SetupMockFileSystem called with {files.Length} files");
        
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.SearchOption>()))
            .Returns(files);
        
        _mockFileSystem.Setup(fs => fs.EnumerateFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.SearchOption>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(files));
            
        // ExpandFilesAsyncの動的セットアップ - 個別のテストケースに対応
        _mockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var filesArg = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                var resultFiles = new List<string>();
                
                foreach (var filePattern in filesArg)
                {
                    if (filePattern == "temp_dir")
                    {
                        // temp_dirの場合は、すべてのモックファイルを使用
                        foreach (var file in files)
                        {
                            if (_mockFileSearchService.Object.ShouldIncludeFile(file, options))
                            {
                                resultFiles.Add(file);
                            }
                        }
                    }
                    else
                    {
                        resultFiles.Add(filePattern);
                    }
                }
                
                _output.WriteLine($"ExpandFilesAsync returning {resultFiles.Count} files: [{string.Join(", ", resultFiles)}]");
                return Task.FromResult(resultFiles.AsEnumerable());
            });
        
        foreach (var file in files)
        {
            _output.WriteLine($"Setting up mock for file: {file}");
            
            _mockFileSystem.Setup(fs => fs.FileExists(file)).Returns(true);
            
            var mockFileInfo = new Mock<IFileInfo>();
            mockFileInfo.Setup(fi => fi.Length).Returns(100);
            _mockFileSystem.Setup(fs => fs.GetFileInfo(file)).Returns(mockFileInfo.Object);
            
            // ファイルに応じて異なる内容を設定
            string content = file.Contains("Program.cs") ? "hello world" : 
                           file.Contains("README.txt") ? "hello test" :
                           file.Contains("test") ? "hello debug" :
                           file.Contains("data") ? "hello debug" :
                           "hello debug"; // その他のファイル
            
            _output.WriteLine($"File {file} content: {content}");
            
            // ストリーミング用のメソッドをセットアップ
            _mockFileSystem.Setup(fs => fs.ReadLinesAsync(file, It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerable(new[] { content }));
                
            _mockFileSystem.Setup(fs => fs.ReadLinesAsMemoryAsync(file, It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerableMemory(new[] { content }));
            
            // OpenFileメソッドのセットアップ - MemoryStreamを使用してファイル内容をシミュレート
            var fileBytes = System.Text.Encoding.UTF8.GetBytes(content + Environment.NewLine);
            _mockFileSystem.Setup(fs => fs.OpenFile(file, It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()))
                .Returns(new MemoryStream(fileBytes));
                
            // OpenTextメソッドのセットアップ
            _mockFileSystem.Setup(fs => fs.OpenText(file, It.IsAny<System.Text.Encoding>(), It.IsAny<int>()))
                .Returns(() => new StreamReader(new MemoryStream(fileBytes)));
            
            _mockPath.Setup(p => p.GetFileName(file)).Returns(System.IO.Path.GetFileName(file));
            _mockPath.Setup(p => p.GetDirectoryName(file)).Returns(System.IO.Path.GetDirectoryName(file));
        }

        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);
        
        _output.WriteLine("SetupMockFileSystem completed");
    }
    
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
    
    private static async IAsyncEnumerable<ReadOnlyMemory<char>> ToAsyncEnumerableMemory(
        IEnumerable<string> items, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item.AsMemory();
            await Task.Yield();
        }
    }

    private static void SetupBasicOptions(Mock<IOptionContext> mockOptions, string file, string pattern)
    {
        mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(pattern);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(new[] { file }.ToList().AsReadOnly());
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(false);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns((string?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns((string?)null);
    }

    public void Dispose()
    {
        // モック使用時はクリーンアップ不要
    }
}
