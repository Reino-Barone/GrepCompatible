using GrepCompatible.Abstractions;
using GrepCompatible.Core;
using GrepCompatible.CommandLine;
using GrepCompatible.Models;
using GrepCompatible.Strategies;
using GrepCompatible.Constants;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace GrepCompatible.Test.Unit.Core;

/// <summary>
/// GrepEngineテスト共通基底クラス
/// </summary>
public abstract class GrepEngineTestsBase : IDisposable
{
    protected readonly List<string> TempFiles = [];
    protected readonly Mock<IMatchStrategyFactory> MockStrategyFactory = new();
    protected readonly Mock<IMatchStrategy> MockStrategy = new();
    protected readonly Mock<IFileSystem> MockFileSystem = new();
    protected readonly Mock<IPath> MockPathHelper = new();
    protected readonly Mock<IFileSearchService> MockFileSearchService = new();
    protected readonly Mock<IPerformanceOptimizer> MockPerformanceOptimizer = new();
    protected readonly Mock<IMatchResultPool> MockMatchResultPool = new();
    protected readonly ParallelGrepEngine Engine;

    protected GrepEngineTestsBase()
    {
        MockStrategyFactory.Setup(f => f.CreateStrategy(It.IsAny<IOptionContext>()))
            .Returns(MockStrategy.Object);
        
        // MatchStrategyのセットアップ
        MockStrategy.Setup(s => s.FindMatches(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IOptionContext>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .Returns<string, string, IOptionContext, string, int>((line, pattern, options, fileName, lineNumber) => {
                var result = new List<MatchResult>();
                var invertMatch = options.GetFlagValue(OptionNames.InvertMatch);
                
                if (!invertMatch && line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var index = line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    var matchedText = pattern.AsMemory();
                    result.Add(new MatchResult(fileName, lineNumber, line, matchedText, index, index + pattern.Length));
                }
                else if (invertMatch && !line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    // Invert match の場合、行全体がマッチ
                    result.Add(new MatchResult(fileName, lineNumber, line, line.AsMemory(), 0, line.Length));
                }
                
                return result;
            });
        
        MockStrategy.Setup(s => s.CanApply(It.IsAny<IOptionContext>()))
            .Returns(true);
        
        // FileSearchService のセットアップ
        MockFileSearchService.Setup(fs => fs.ExpandFilesAsync(It.IsAny<IOptionContext>(), It.IsAny<CancellationToken>()))
            .Returns<IOptionContext, CancellationToken>((options, cancellationToken) =>
            {
                var files = options.GetStringListArgumentValue(ArgumentNames.Files) ?? new List<string>().AsReadOnly();
                return Task.FromResult(files.AsEnumerable());
            });

        // Performance Optimizer のセットアップ - これが重要！
        MockPerformanceOptimizer.Setup(po => po.CalculateOptimalParallelism(It.IsAny<int>()))
            .Returns<int>(fileCount => Math.Max(1, Math.Min(Environment.ProcessorCount, fileCount == 0 ? 1 : fileCount)));
        
        MockPerformanceOptimizer.Setup(po => po.GetOptimalBufferSize(It.IsAny<long>()))
            .Returns(4096);
        
        // FileSystemのGetFileInfoのセットアップ
        MockFileSystem.Setup(fs => fs.GetFileInfo(It.IsAny<string>()))
            .Returns<string>(path => CreateMockFileInfo(path, 1024, true));

        Engine = new ParallelGrepEngine(
            MockStrategyFactory.Object,
            MockFileSystem.Object,
            MockPathHelper.Object,
            MockFileSearchService.Object,
            MockPerformanceOptimizer.Object,
            MockMatchResultPool.Object);
    }

    /// <summary>
    /// 配列をIAsyncEnumerableに変換するヘルパーメソッド
    /// </summary>
    protected static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
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

    protected string CreateTempFile(string content, string extension = ".txt")
    {
        var tempFile = $"temp_{Guid.NewGuid()}{extension}";
        var lines = content.Split('\n');
        
        // モックファイルシステムにファイル読み込みをセットアップ
        MockFileSystem.Setup(fs => fs.ReadLinesAsync(tempFile, It.IsAny<CancellationToken>()))
                      .Returns(ToAsyncEnumerable(lines));
        MockFileSystem.Setup(fs => fs.FileExists(tempFile)).Returns(true);
        
        // OpenFileでの読み取り用にもコンテンツを設定
        MockFileSystem.Setup(fs => fs.OpenFile(
            tempFile, 
            It.IsAny<FileMode>(), 
            It.IsAny<FileAccess>(), 
            It.IsAny<FileShare>(), 
            It.IsAny<int>(), 
            It.IsAny<FileOptions>()))
            .Returns(() => {
                var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                return stream;
            });
        
        TempFiles.Add(tempFile);
        return tempFile;
    }

    protected string CreateTempDirectory()
    {
        var tempDir = $"temp_dir_{Guid.NewGuid()}";
        // モックでは特別な処理は不要
        TempFiles.Add(tempDir);
        return tempDir;
    }

    protected static void SetupBasicOptions(Mock<IOptionContext> mockOptions, string file, string pattern)
    {
        SetupBasicOptions(mockOptions, new[] { file }, pattern);
    }

    protected static void SetupBasicOptions(Mock<IOptionContext> mockOptions, string[] files, string pattern)
    {
        mockOptions.Setup(o => o.GetStringArgumentValue(ArgumentNames.Pattern)).Returns(pattern);
        mockOptions.Setup(o => o.GetStringListArgumentValue(ArgumentNames.Files))
            .Returns(files.ToList().AsReadOnly());
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.InvertMatch)).Returns(false);
        mockOptions.Setup(o => o.GetFlagValue(OptionNames.RecursiveSearch)).Returns(false);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.MaxCount)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.Context)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextBefore)).Returns((int?)null);
        mockOptions.Setup(o => o.GetIntValue(OptionNames.ContextAfter)).Returns((int?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.ExcludePattern)).Returns((string?)null);
        mockOptions.Setup(o => o.GetStringValue(OptionNames.IncludePattern)).Returns((string?)null);
    }

    /// <summary>
    /// モックFileInfoを作成
    /// </summary>
    private static IFileInfo CreateMockFileInfo(string path, long length, bool exists)
    {
        var mockFileInfo = new Mock<IFileInfo>();
        mockFileInfo.Setup(fi => fi.FullName).Returns(path);
        mockFileInfo.Setup(fi => fi.Name).Returns(Path.GetFileName(path));
        mockFileInfo.Setup(fi => fi.Length).Returns(length);
        mockFileInfo.Setup(fi => fi.Exists).Returns(exists);
        return mockFileInfo.Object;
    }

    public virtual void Dispose()
    {
        // モックなので特別なクリア処理は不要
        TempFiles.Clear();
    }
}
