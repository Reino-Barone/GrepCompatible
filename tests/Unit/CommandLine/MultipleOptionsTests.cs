using GrepCompatible.Abstractions.CommandLine;
using GrepCompatible.Abstractions.Constants;
using Xunit;
using System.Linq;

namespace GrepCompatible.Test.Unit.CommandLine
{
    /// <summary>
    /// 複数オプション指定のテスト
    /// </summary>
    public class MultipleOptionsTests
    {
        [Fact]
        public void Parse_MultipleIncludeOptions_AccumulatesAllValues()
        {
            // Arrange
            var command = new GrepCommand();
            var args = new[] { "searchpattern", "--include=*.cs", "--include=*.js", "--include=*.ts", "-r", "." };

            // Act
            var result = command.Parse(args);

            // Assert
            Assert.True(result.IsSuccess, $"Parsing failed: {result.ErrorMessage}");
            
            var context = command.ToOptionContext();
            var includeValues = context.GetAllStringValues(OptionNames.IncludePattern);
            
            Assert.Equal(3, includeValues.Count);
            Assert.Contains("*.cs", includeValues);
            Assert.Contains("*.js", includeValues);
            Assert.Contains("*.ts", includeValues);
        }

        [Fact]
        public void Parse_MultipleExcludeOptions_AccumulatesAllValues()
        {
            // Arrange
            var command = new GrepCommand();
            var args = new[] { "searchpattern", "--exclude=*.log", "--exclude=*.tmp", "--exclude=*.bak", "-r", "." };

            // Act
            var result = command.Parse(args);

            // Assert
            Assert.True(result.IsSuccess, $"Parsing failed: {result.ErrorMessage}");
            
            var context = command.ToOptionContext();
            var excludeValues = context.GetAllStringValues(OptionNames.ExcludePattern);
            
            Assert.Equal(3, excludeValues.Count);
            Assert.Contains("*.log", excludeValues);
            Assert.Contains("*.tmp", excludeValues);
            Assert.Contains("*.bak", excludeValues);
        }

        [Fact]
        public void Parse_MixedIncludeAndExcludeOptions_AccumulatesSeparately()
        {
            // Arrange
            var command = new GrepCommand();
            var args = new[] { "searchpattern", "--include=*.cs", "--exclude=*.log", "--include=*.js", "--exclude=*.tmp", "-r", "." };

            // Act
            var result = command.Parse(args);

            // Assert
            Assert.True(result.IsSuccess, $"Parsing failed: {result.ErrorMessage}");
            
            var context = command.ToOptionContext();
            var includeValues = context.GetAllStringValues(OptionNames.IncludePattern);
            var excludeValues = context.GetAllStringValues(OptionNames.ExcludePattern);
            
            Assert.Equal(2, includeValues.Count);
            Assert.Contains("*.cs", includeValues);
            Assert.Contains("*.js", includeValues);
            
            Assert.Equal(2, excludeValues.Count);
            Assert.Contains("*.log", excludeValues);
            Assert.Contains("*.tmp", excludeValues);
        }

        [Fact]
        public void Parse_SingleOption_StillWorksAsExpected()
        {
            // Arrange
            var command = new GrepCommand();
            var args = new[] { "searchpattern", "--include=*.cs", "-r", "." };

            // Act
            var result = command.Parse(args);

            // Assert
            Assert.True(result.IsSuccess, $"Parsing failed: {result.ErrorMessage}");
            
            var context = command.ToOptionContext();
            var includeValues = context.GetAllStringValues(OptionNames.IncludePattern);
            var singleValue = context.GetStringValue(OptionNames.IncludePattern);
            
            Assert.Single(includeValues);
            Assert.Contains("*.cs", includeValues);
            Assert.Equal("*.cs", singleValue);
        }

        [Fact]
        public void Parse_NoOptions_ReturnsEmptyList()
        {
            // Arrange
            var command = new GrepCommand();
            var args = new[] { "searchpattern", "-r", "." };

            // Act
            var result = command.Parse(args);

            // Assert
            Assert.True(result.IsSuccess, $"Parsing failed: {result.ErrorMessage}");
            
            var context = command.ToOptionContext();
            var includeValues = context.GetAllStringValues(OptionNames.IncludePattern);
            var excludeValues = context.GetAllStringValues(OptionNames.ExcludePattern);
            
            Assert.Empty(includeValues);
            Assert.Empty(excludeValues);
        }
    }
}