using System.IO;
using GrepCompatible.Abstractions;

namespace GrepCompatible.Test.Infrastructure;

/// <summary>
/// パス操作のモックヘルパー
/// </summary>
public class MockPathHelper : IPath
{
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    
    public string GetFileName(string path) => Path.GetFileName(path);
    
    public string Combine(params string[] paths) => Path.Combine(paths);
}
