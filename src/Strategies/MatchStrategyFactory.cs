using GrepCompatible.Models;

namespace GrepCompatible.Strategies;

/// <summary>
/// マッチング戦略のファクトリー実装
/// </summary>
public class MatchStrategyFactory : IMatchStrategyFactory
{
    private readonly List<IMatchStrategy> _strategies = [];
    
    public MatchStrategyFactory()
    {
        // デフォルトの戦略を登録
        RegisterStrategy(new FixedStringMatchStrategy());
        RegisterStrategy(new WholeWordMatchStrategy());
        RegisterStrategy(new RegexMatchStrategy());
    }
    
    public IMatchStrategy CreateStrategy(DynamicOptions options)
    {
        // 優先度順で適用可能な戦略を選択
        var applicableStrategy = _strategies.FirstOrDefault(s => s.CanApply(options));
        
        if (applicableStrategy != null)
            return applicableStrategy;
        
        // フォールバック: 正規表現戦略を返す
        return new RegexMatchStrategy();
    }
    
    public void RegisterStrategy(IMatchStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        
        // 既に同じ型の戦略が登録されている場合は置換
        var existingIndex = _strategies.FindIndex(s => s.GetType() == strategy.GetType());
        if (existingIndex >= 0)
        {
            _strategies[existingIndex] = strategy;
        }
        else
        {
            _strategies.Add(strategy);
        }
    }
}
