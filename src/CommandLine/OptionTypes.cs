using GrepCompatible.Constants;

namespace GrepCompatible.CommandLine;

/// <summary>
/// フラグオプション（値を持たないオプション）
/// </summary>
public class FlagOption : Option<bool>
{
    public FlagOption(OptionNames name, string description, bool defaultValue = false, 
                     string? shortName = null, string? longName = null) 
        : base(name, description, defaultValue, shortName, longName)
    {
    }
    
    public override bool TryParse(string? value)
    {
        // フラグオプションは値を持たないので、常にtrueに設定
        Value = true;
        IsSet = true;
        return true;
    }
    
    public override string GetUsageString()
    {
        return $"{base.GetUsageString()} (flag)";
    }
}

/// <summary>
/// 文字列オプション
/// </summary>
public class StringOption : Option<string>
{
    public StringOption(OptionNames name, string description, string defaultValue = "", 
                       string? shortName = null, string? longName = null, bool isRequired = false) 
        : base(name, description, defaultValue, shortName, longName, isRequired)
    {
    }
    
    public override bool TryParse(string? value)
    {
        if (value == null && IsRequired)
            return false;
        
        Value = value ?? DefaultValue;
        IsSet = true;
        return true;
    }
    
    public override string GetUsageString()
    {
        return $"{base.GetUsageString()} <string>";
    }
}

/// <summary>
/// 整数オプション
/// </summary>
public class IntegerOption : Option<int>
{
    public int MinValue { get; }
    public int MaxValue { get; }
    
    public IntegerOption(OptionNames name, string description, int defaultValue = 0, 
                        string? shortName = null, string? longName = null, bool isRequired = false,
                        int minValue = int.MinValue, int maxValue = int.MaxValue) 
        : base(name, description, defaultValue, shortName, longName, isRequired)
    {
        MinValue = minValue;
        MaxValue = maxValue;
    }
    
    public override bool TryParse(string? value)
    {
        if (value == null)
        {
            if (IsRequired) return false;
            Value = DefaultValue;
            IsSet = true;
            return true;
        }
        
        if (!int.TryParse(value, out var intValue))
            return false;
        
        if (intValue < MinValue || intValue > MaxValue)
            return false;
        
        Value = intValue;
        IsSet = true;
        return true;
    }
    
    public override string GetUsageString()
    {
        var range = MinValue == int.MinValue && MaxValue == int.MaxValue 
            ? "" 
            : $" ({MinValue}-{MaxValue})";
        return $"{base.GetUsageString()} <int{range}>";
    }
}

/// <summary>
/// Null許容整数オプション
/// </summary>
public class NullableIntegerOption : Option<int?>
{
    public int MinValue { get; }
    public int MaxValue { get; }
    
    public NullableIntegerOption(OptionNames name, string description, int? defaultValue = null, 
                                string? shortName = null, string? longName = null, bool isRequired = false,
                                int minValue = int.MinValue, int maxValue = int.MaxValue) 
        : base(name, description, defaultValue, shortName, longName, isRequired)
    {
        MinValue = minValue;
        MaxValue = maxValue;
    }
    
    public override bool TryParse(string? value)
    {
        if (value == null)
        {
            if (IsRequired) return false;
            Value = DefaultValue;
            IsSet = true;
            return true;
        }
        
        if (!int.TryParse(value, out var intValue))
            return false;
        
        if (intValue < MinValue || intValue > MaxValue)
            return false;
        
        Value = intValue;
        IsSet = true;
        return true;
    }
    
    public override string GetUsageString()
    {
        var range = MinValue == int.MinValue && MaxValue == int.MaxValue 
            ? "" 
            : $" ({MinValue}-{MaxValue})";
        return $"{base.GetUsageString()} <int{range}>";
    }
}
