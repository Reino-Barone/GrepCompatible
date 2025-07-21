using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrepCompatible.Abstractions;
using Moq;

namespace GrepCompatible.Test.Infrastructure;

/// <summary>
/// テスト用ファイルシステムビルダー - Mock<IFileSystem>のラッパー
/// </summary>
public class FileSystemTestBuilder
{
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly Dictionary<string, string> _files;
    private readonly HashSet<string> _directories;
    private string? _standardInput;

    public FileSystemTestBuilder()
    {
        _mockFileSystem = new Mock<IFileSystem>();
        _files = new Dictionary<string, string>();
        _directories = new HashSet<string>();
        SetupBasicMockBehavior();
    }

    private void SetupBasicMockBehavior()
    {
        // GetFileInfoの設定
        _mockFileSystem.Setup(fs => fs.GetFileInfo(It.IsAny<string>()))
            .Returns<string>(path => 
            {
                if (_files.ContainsKey(path))
                {
                    var mockFileInfo = new Mock<IFileInfo>();
                    mockFileInfo.Setup(fi => fi.Exists).Returns(true);
                    mockFileInfo.Setup(fi => fi.Length).Returns(System.Text.Encoding.UTF8.GetByteCount(_files[path]));
                    mockFileInfo.Setup(fi => fi.Name).Returns(Path.GetFileName(path));
                    mockFileInfo.Setup(fi => fi.FullName).Returns(path);
                    return mockFileInfo.Object;
                }
                
                var nonExistentFileInfo = new Mock<IFileInfo>();
                nonExistentFileInfo.Setup(fi => fi.Exists).Returns(false);
                nonExistentFileInfo.Setup(fi => fi.Length).Returns(0); // 存在しないファイルの長さは0
                nonExistentFileInfo.Setup(fi => fi.Name).Returns(Path.GetFileName(path));
                nonExistentFileInfo.Setup(fi => fi.FullName).Returns(path);
                return nonExistentFileInfo.Object;
            });

        // OpenFileの設定
        _mockFileSystem.Setup(fs => fs.OpenFile(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()))
            .Returns<string, FileMode, FileAccess, FileShare, int, FileOptions>((path, mode, access, share, bufferSize, options) =>
            {
                if (_files.TryGetValue(path, out var content))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                    return new MemoryStream(bytes);
                }
                throw new FileNotFoundException($"File not found: {path}");
            });

        // OpenTextの設定
        _mockFileSystem.Setup(fs => fs.OpenText(It.IsAny<string>(), It.IsAny<System.Text.Encoding>(), It.IsAny<int>()))
            .Returns<string, System.Text.Encoding, int>((path, encoding, bufferSize) =>
            {
                if (_files.TryGetValue(path, out var content))
                {
                    var bytes = encoding.GetBytes(content);
                    var stream = new MemoryStream(bytes);
                    return new StreamReader(stream, encoding);
                }
                throw new FileNotFoundException($"File not found: {path}");
            });

        // ReadLinesAsyncの設定
        _mockFileSystem.Setup(fs => fs.ReadLinesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, cancellationToken) =>
            {
                if (_files.TryGetValue(path, out var content))
                {
                    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return CreateAsyncEnumerable(lines);
                }
                return CreateAsyncEnumerable(Array.Empty<string>());
            });

        // ReadLinesAsMemoryAsyncの設定
        _mockFileSystem.Setup(fs => fs.ReadLinesAsMemoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken cancellationToken) =>
            {
                if (_files.TryGetValue(path, out var content))
                {
                    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return CreateAsyncEnumerableMemory(lines);
                }
                return CreateAsyncEnumerableMemory(Array.Empty<string>());
            });

        // ReadStandardInputAsyncの設定
        _mockFileSystem.Setup(fs => fs.ReadStandardInputAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(cancellationToken => 
            {
                if (_standardInput != null)
                {
                    var lines = _standardInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return CreateAsyncEnumerable(lines);
                }
                return CreateAsyncEnumerable(Array.Empty<string>());
            });

        // GetStandardInputの設定
        _mockFileSystem.Setup(fs => fs.GetStandardInput())
            .Returns(() =>
            {
                if (_standardInput != null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(_standardInput);
                    var stream = new MemoryStream(bytes);
                    return new StreamReader(stream);
                }
                return new StreamReader(new MemoryStream());
            });

        // DirectoryExistsの設定
        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns<string>(path => _directories.Contains(path));

        // EnumerateFilesの設定
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()))
            .Returns<string, string, SearchOption>((path, searchPattern, searchOption) =>
            {
                // 指定されたディレクトリまたはその子ディレクトリ内のファイルを列挙
                var result = new List<string>();
                
                foreach (var filePath in _files.Keys)
                {
                    var dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
                    
                    if (searchOption == SearchOption.AllDirectories)
                    {
                        // 再帰検索の場合、パスが開始ディレクトリ以下にあるかチェック
                        if (dirPath.StartsWith(path, StringComparison.OrdinalIgnoreCase) || path == "." || path == string.Empty)
                        {
                            if (MatchesPattern(Path.GetFileName(filePath), searchPattern))
                            {
                                result.Add(filePath);
                            }
                        }
                    }
                    else
                    {
                        // 現在のディレクトリのみ
                        if (string.Equals(dirPath, path, StringComparison.OrdinalIgnoreCase) || (path == "." && string.IsNullOrEmpty(dirPath)))
                        {
                            if (MatchesPattern(Path.GetFileName(filePath), searchPattern))
                            {
                                result.Add(filePath);
                            }
                        }
                    }
                }
                
                return result;
            });

        // EnumerateFilesAsyncの設定
        _mockFileSystem.Setup(fs => fs.EnumerateFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, SearchOption, CancellationToken>((path, searchPattern, searchOption, cancellationToken) =>
            {
                // 指定されたディレクトリまたはその子ディレクトリ内のファイルを列挙
                var result = new List<string>();
                
                foreach (var filePath in _files.Keys)
                {
                    var dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
                    
                    if (searchOption == SearchOption.AllDirectories)
                    {
                        // 再帰検索の場合、パスが開始ディレクトリ以下にあるかチェック
                        if (dirPath.StartsWith(path, StringComparison.OrdinalIgnoreCase) || path == "." || path == string.Empty)
                        {
                            if (MatchesPattern(Path.GetFileName(filePath), searchPattern))
                            {
                                result.Add(filePath);
                            }
                        }
                    }
                    else
                    {
                        // 現在のディレクトリのみ
                        if (string.Equals(dirPath, path, StringComparison.OrdinalIgnoreCase) || (path == "." && string.IsNullOrEmpty(dirPath)))
                        {
                            if (MatchesPattern(Path.GetFileName(filePath), searchPattern))
                            {
                                result.Add(filePath);
                            }
                        }
                    }
                }
                
                return CreateAsyncEnumerable(result);
            });
    }

    private IAsyncEnumerable<string> CreateAsyncEnumerable(IEnumerable<string> source)
    {
        return CreateAsyncEnumerableImpl(source);
    }

    private async IAsyncEnumerable<string> CreateAsyncEnumerableImpl(IEnumerable<string> source)
    {
        await Task.Yield(); // Make it actually async
        foreach (var item in source)
        {
            yield return item;
        }
    }

    private IAsyncEnumerable<ReadOnlyMemory<char>> CreateAsyncEnumerableMemory(IEnumerable<string> source)
    {
        return CreateAsyncEnumerableMemoryImpl(source);
    }

    private async IAsyncEnumerable<ReadOnlyMemory<char>> CreateAsyncEnumerableMemoryImpl(IEnumerable<string> source)
    {
        await Task.Yield(); // Make it actually async
        foreach (var item in source)
        {
            yield return item.AsMemory();
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*" || pattern == "*.*")
        {
            return true;
        }
        
        // 簡単なワイルドカードマッチング
        // より複雑なパターンが必要な場合は、System.IO.Enumeration.FileSystemName.MatchesSimpleExpression を使用
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            // 正規表現に変換
            var regexPattern = "^" + pattern.Replace(".", @"\.").Replace("*", ".*").Replace("?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ファイルとその内容を追加
    /// </summary>
    public FileSystemTestBuilder WithFile(string path, string content)
    {
        _files[path] = content;
        
        // ファイルのディレクトリパスも自動的に追加
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
            
            // 親ディレクトリも追加（階層構造を保持）
            var currentDir = directory;
            while (!string.IsNullOrEmpty(currentDir) && !string.Equals(currentDir, Path.GetDirectoryName(currentDir)))
            {
                _directories.Add(currentDir);
                currentDir = Path.GetDirectoryName(currentDir);
            }
        }
        
        return this;
    }

    /// <summary>
    /// 複数のファイルパスを一括で追加
    /// </summary>
    public FileSystemTestBuilder WithFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            // 既にファイルが存在する場合は内容を保持、存在しない場合はデフォルト内容を設定
            if (!_files.ContainsKey(path))
            {
                WithFile(path, "default content");
            }
        }
        return this;
    }

    /// <summary>
    /// ディレクトリを追加
    /// </summary>
    public FileSystemTestBuilder WithDirectory(string path)
    {
        _directories.Add(path);
        return this;
    }

    /// <summary>
    /// ファイル情報を設定
    /// </summary>
    public FileSystemTestBuilder WithFileInfo(string path, long length, DateTime? lastWriteTime = null)
    {
        // Mock<IFileSystem>では、ファイル情報は自動的に管理されます
        return this;
    }

    /// <summary>
    /// 標準入力の内容を設定
    /// </summary>
    public FileSystemTestBuilder WithStandardInput(string content)
    {
        _standardInput = content;
        return this;
    }

    /// <summary>
    /// 構築されたファイルシステムを取得
    /// </summary>
    public IFileSystem Build()
    {
        return _mockFileSystem.Object;
    }

    // ========== ファクトリメソッド ==========

    /// <summary>
    /// 基本的なプロジェクト構造を持つファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateBasicProject()
    {
        return new FileSystemTestBuilder()
            .WithDirectory(".")
            .WithDirectory("src")
            .WithDirectory("tests")
            .WithFile("README.md", "# Project Title")
            .WithFile("src/Program.cs", "using System;\n\nclass Program { static void Main() { } }")
            .WithFile("tests/UnitTest1.cs", "using Xunit;\n\npublic class UnitTest1 { }");
    }

    /// <summary>
    /// 複数パターンテスト用のファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateMultiplePatternTestFiles()
    {
        return new FileSystemTestBuilder()
            .WithDirectory(".")
            .WithFile("test.cs", "test content")
            .WithFile("test.js", "test content")
            .WithFile("test.txt", "test content")
            .WithFile("test.log", "test content");
    }

    /// <summary>
    /// サブディレクトリを含む複数パターンテスト用のファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateNestedMultiplePatternTestFiles()
    {
        return new FileSystemTestBuilder()
            .WithDirectory(".")
            .WithDirectory("src")
            .WithDirectory("tests")
            .WithFile("src/test.cs", "test content")
            .WithFile("src/test.js", "test content")
            .WithFile("tests/test.cs", "test content")
            .WithFile("tests/test.js", "test content");
    }

    /// <summary>
    /// 大きなファイルを含むテスト用のファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateLargeFileTestSystem()
    {
        return new FileSystemTestBuilder()
            .WithDirectory(".")
            .WithFile("large.txt", string.Join("\n", Enumerable.Range(1, 10000).Select(i => $"Line {i} with test content")));
    }

    /// <summary>
    /// 標準入力テスト用のファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateStandardInputTestSystem(string standardInput)
    {
        return new FileSystemTestBuilder()
            .WithStandardInput(standardInput);
    }

    /// <summary>
    /// 空のディレクトリ構造を持つファイルシステムを作成
    /// </summary>
    public static FileSystemTestBuilder CreateEmptyDirectoryStructure()
    {
        return new FileSystemTestBuilder()
            .WithDirectory(".")
            .WithDirectory("empty")
            .WithDirectory("also_empty");
    }
}
