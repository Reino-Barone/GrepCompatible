using GrepCompatible.Abstractions.Constants;

namespace GrepCompatible.Abstractions.CommandLine;

/// <summary>
/// オプションの抽象基底クラス
/// </summary>
public abstract class Option
{
    /// <summary>
    /// オプションの名前
    /// </summary>
    public OptionNames Name { get; }
    
    /// <summary>
    /// オプションの説明
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// 短いオプション名（例: -h）
    /// </summary>
    public string? ShortName { get; }
    
    /// <summary>
    /// 長いオプション名（例: --help）
    /// </summary>
    public string? LongName { get; }
    
    /// <summary>
    /// オプションが必須かどうか
    /// </summary>
    public bool IsRequired { get; }
    
    /// <summary>
    /// オプションが設定されているかどうか
    /// </summary>
    public bool IsSet { get; protected set; }

    protected Option(OptionNames name, string description, string? shortName = null, string? longName = null, bool isRequired = false)
    {
        Name = name;
        Description = description;
        ShortName = shortName;
        LongName = longName;
        IsRequired = isRequired;
    }
    
    /// <summary>
    /// オプションの値を解析
    /// </summary>
    /// <param name="value">解析する値</param>
    /// <returns>解析が成功した場合はtrue</returns>
    public abstract bool TryParse(string? value);
    
    /// <summary>
    /// オプションをリセット
    /// </summary>
    public virtual void Reset()
    {
        IsSet = false;
    }
    
    /// <summary>
    /// オプションの文字列表現を取得
    /// </summary>
    public virtual string GetUsageString()
    {
        var names = new List<string>();
        if (ShortName != null) names.Add(ShortName);
        if (LongName != null) names.Add(LongName);
        
        var nameString = string.Join(", ", names);
        return IsRequired ? $"{nameString} (required)" : nameString;
    }
}

/// <summary>
/// 型付きオプションの抽象基底クラス
/// </summary>
/// <typeparam name="T">オプションの値の型</typeparam>
public abstract class Option<T> : Option
{
    /// <summary>
    /// オプションの値
    /// </summary>
    public T Value { get; protected set; }
    
    /// <summary>
    /// デフォルト値
    /// </summary>
    public T DefaultValue { get; }

    protected Option(OptionNames name, string description, T defaultValue, 
                    string? shortName = null, string? longName = null, bool isRequired = false) 
        : base(name, description, shortName, longName, isRequired)
    {
        DefaultValue = defaultValue;
        Value = defaultValue;
    }
    
    /// <summary>
    /// オプションをリセット
    /// </summary>
    public override void Reset()
    {
        base.Reset();
        Value = DefaultValue;
    }
}
