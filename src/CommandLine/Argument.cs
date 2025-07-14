namespace GrepCompatible.CommandLine;

/// <summary>
/// 引数（非オプション）の抽象基底クラス
/// </summary>
public abstract class Argument
{
    /// <summary>
    /// 引数の名前
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// 引数の説明
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// 引数が必須かどうか
    /// </summary>
    public bool IsRequired { get; }
    
    /// <summary>
    /// 引数が設定されているかどうか
    /// </summary>
    public bool IsSet { get; protected set; }

    protected Argument(string name, string description, bool isRequired = true)
    {
        Name = name;
        Description = description;
        IsRequired = isRequired;
    }
    
    /// <summary>
    /// 引数の値を解析
    /// </summary>
    /// <param name="value">解析する値</param>
    /// <returns>解析が成功した場合はtrue</returns>
    public abstract bool TryParse(string value);
    
    /// <summary>
    /// 引数をリセット
    /// </summary>
    public virtual void Reset()
    {
        IsSet = false;
    }
    
    /// <summary>
    /// 引数の使用方法文字列を取得
    /// </summary>
    public virtual string GetUsageString()
    {
        return IsRequired ? $"<{Name}>" : $"[{Name}]";
    }
}

/// <summary>
/// 型付き引数の抽象基底クラス
/// </summary>
/// <typeparam name="T">引数の値の型</typeparam>
public abstract class Argument<T> : Argument
{
    /// <summary>
    /// 引数の値
    /// </summary>
    public T Value { get; protected set; }
    
    /// <summary>
    /// デフォルト値
    /// </summary>
    public T DefaultValue { get; }

    protected Argument(string name, string description, T defaultValue, bool isRequired = true) 
        : base(name, description, isRequired)
    {
        DefaultValue = defaultValue;
        Value = defaultValue;
    }
    
    /// <summary>
    /// 引数をリセット
    /// </summary>
    public override void Reset()
    {
        base.Reset();
        Value = DefaultValue;
    }
}

/// <summary>
/// 文字列引数
/// </summary>
public class StringArgument : Argument<string>
{
    public StringArgument(string name, string description, string defaultValue = "", bool isRequired = true) 
        : base(name, description, defaultValue, isRequired)
    {
    }
    
    public override bool TryParse(string value)
    {
        if (string.IsNullOrEmpty(value) && IsRequired)
            return false;
        
        Value = value ?? DefaultValue;
        IsSet = true;
        return true;
    }
}

/// <summary>
/// 文字列リスト引数（複数の値を受け取る）
/// </summary>
public class StringListArgument : Argument<IReadOnlyList<string>>
{
    private readonly List<string> _values = [];
    
    public StringListArgument(string name, string description, bool isRequired = true) 
        : base(name, description, new List<string>().AsReadOnly(), isRequired)
    {
    }
    
    public override bool TryParse(string value)
    {
        if (string.IsNullOrEmpty(value) && IsRequired && _values.Count == 0)
            return false;
        
        if (!string.IsNullOrEmpty(value))
        {
            _values.Add(value);
        }
        
        Value = _values.AsReadOnly();
        IsSet = true;
        return true;
    }
    
    public override void Reset()
    {
        base.Reset();
        _values.Clear();
        Value = _values.AsReadOnly();
    }
    
    public override string GetUsageString()
    {
        return IsRequired ? $"<{Name}...>" : $"[{Name}...]";
    }
}
