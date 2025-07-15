# GrepCompatible

A high-performance, POSIX-compatible grep implementation written in C# for .NET 9.0.

📖 **Languages**: [English](README.md) | [日本語](README.ja.md)

---

## Features

### Core Functionality

- **POSIX-compatible**: Implements standard grep command-line options and behavior
- **High Performance**: Optimized with parallel processing and memory-efficient algorithms
- **Cross-platform**: Runs on Windows, Linux, and macOS with .NET 9.0
- **Pattern Matching**: Supports regular expressions, fixed strings, and extended regex patterns

### Search Options

- **Case-insensitive search** (`-i`, `--ignore-case`)
- **Invert matching** (`-v`, `--invert-match`) - Select non-matching lines
- **Line numbers** (`-n`, `--line-number`) - Display line numbers with matches
- **Count only** (`-c`, `--count`) - Show only count of matching lines per file
- **Filename only** (`-l`, `--files-with-matches`) - Show only filenames containing matches
- **Suppress filename** (`-h`, `--no-filename`) - Hide filename prefix in output
- **Silent mode** (`-q`, `--quiet`) - Suppress all normal output

### Pattern Types

- **Extended regular expressions** (`-E`, `--extended-regexp`)
- **Fixed strings** (`-F`, `--fixed-strings`) - Treat pattern as literal strings
- **Whole word matching** (`-w`, `--word-regexp`) - Match only whole words

### File Operations

- **Recursive search** (`-r`, `--recursive`) - Search directories recursively
- **Include patterns** (`--include`) - Search only files matching pattern
- **Exclude patterns** (`--exclude`) - Skip files matching pattern
- **Standard input support** - Read from stdin when no files specified

### Output Control

- **Context lines** (`-C`, `--context`) - Show N lines of context around matches
- **Before context** (`-B`, `--before-context`) - Show N lines before matches
- **After context** (`-A`, `--after-context`) - Show N lines after matches
- **Max count** (`-m`, `--max-count`) - Stop after N matches

## Installation

### Prerequisites

- .NET 9.0 SDK or later

### Build from Source

```bash
git clone https://github.com/Reino-Barone/GrepCompatible.git
cd GrepCompatible
dotnet build -c Release
```

### Run

```bash
dotnet run --project src -- [OPTIONS] PATTERN [FILE...]
```

Or build and use the executable:

```bash
dotnet publish -c Release -o ./publish
./publish/GrepCompatible [OPTIONS] PATTERN [FILE...]
```

## Usage

### Basic Usage

```bash
# Search for "hello" in file.txt
GrepCompatible hello file.txt

# Search for "hello" in all .txt files
GrepCompatible hello *.txt

# Case-insensitive search
GrepCompatible -i hello file.txt

# Show line numbers
GrepCompatible -n hello file.txt

# Recursive search in directory
GrepCompatible -r hello /path/to/directory

# Search with context lines
GrepCompatible -C 3 hello file.txt
```

### Advanced Usage

```bash
# Extended regex pattern
GrepCompatible -E "hello|world" file.txt

# Fixed string search (no regex)
GrepCompatible -F "hello.world" file.txt

# Whole word matching
GrepCompatible -w hello file.txt

# Count matches only
GrepCompatible -c hello file.txt

# Show only filenames with matches
GrepCompatible -l hello *.txt

# Invert match (non-matching lines)
GrepCompatible -v hello file.txt

# Include/exclude file patterns
GrepCompatible --include="*.cs" --exclude="*.Test.cs" hello /path/to/source
```

### Reading from Standard Input

```bash
# Read from stdin
echo "hello world" | GrepCompatible hello

# Use with pipes
cat file.txt | GrepCompatible -i hello
```

## Architecture

### Core Components

- **GrepApplication**: Main application entry point and orchestration
- **GrepEngine**: Parallel processing engine for file searching
- **MatchStrategy**: Pluggable pattern matching strategies
- **OutputFormatter**: POSIX-compliant output formatting
- **CommandLine**: Robust command-line argument parsing

### Performance Optimizations

- **Parallel Processing**: Multi-threaded file processing
- **Memory Management**: Uses `ArrayPool<T>` and `Span<T>` for efficient memory usage
- **Async I/O**: Non-blocking file operations
- **Optimized String Operations**: Efficient string searching and matching

## Development

### Project Structure

```text
GrepCompatible/
├── src/
│   ├── GrepCompatible.csproj
│   ├── Program.cs
│   ├── Abstractions/        # Interfaces and contracts
│   ├── CommandLine/         # Command-line parsing
│   ├── Constants/           # Shared constants
│   ├── Core/               # Core application logic
│   ├── Models/             # Data models
│   └── Strategies/         # Pattern matching strategies
├── tests/                  # Unit tests
└── GrepCompatible.sln
```

### Running Tests

```bash
dotnet test
```

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish for distribution
dotnet publish -c Release -r win-x64 --self-contained
```

## Performance

GrepCompatible is designed for high performance with:

- Parallel file processing across multiple CPU cores
- Memory-efficient string operations using `Span<T>`
- Optimized buffer management with `ArrayPool<T>`
- Async I/O operations to prevent blocking

## Compatibility

### POSIX Compliance

- Follows POSIX grep specification
- Compatible exit codes (0 for matches, 1 for no matches, 2 for errors)
- Standard option formats (`-h`, `--help`, etc.)

### Platforms

- Windows 10/11
- Linux (Ubuntu, CentOS, etc.)
- macOS 10.15+

### .NET Version

- Requires .NET 9.0 or later
- Uses modern C# features (primary constructors, record types, etc.)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Inspired by GNU grep and other POSIX-compliant implementations
- Built with modern C# and .NET performance best practices
- Designed for both educational and production use
