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
                    mockFileInfo.Setup(fi => fi.Length).Returns(_files[path].Length);
                    mockFileInfo.Setup(fi => fi.Name).Returns(Path.GetFileName(path));
                    mockFileInfo.Setup(fi => fi.FullName).Returns(path);
                    return mockFileInfo.Object;
                }
                
                var nonExistentFileInfo = new Mock<IFileInfo>();
                nonExistentFileInfo.Setup(fi => fi.Exists).Returns(false);
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

    /// <summary>
    /// ファイルとその内容を追加
    /// </summary>
    public FileSystemTestBuilder WithFile(string path, string content)
    {
        _files[path] = content;
        return this;
    }

    /// <summary>
    /// 複数のファイルパスを一括で追加
    /// </summary>
    public FileSystemTestBuilder WithFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            _files[path] = "default content";
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
}
