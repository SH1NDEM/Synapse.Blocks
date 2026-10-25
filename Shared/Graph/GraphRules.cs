namespace Synapse.Blocks.Graph;

public enum GraphRuleKind
{
    /// <summary>В программе не меньше Count подходящих блоков.</summary>
    MinNodes,
    /// <summary>В программе не больше Count подходящих блоков (0 — блок запрещён).</summary>
    MaxNodes,
    /// <summary>Хотя бы в одном теле цикла не меньше Count подходящих блоков.</summary>
    MinNodesInScope
}

/// <summary>
/// Учебное ограничение уровня: проверяет не ответ, а способ решения.
/// Target — TypeId блока (<c>"loop"</c>) или категория (<c>"category:math"</c>).
/// Заменяет отдельные поля RequiredBlocks / RequiredLoopCount / MaxOperationBlocks.
/// </summary>
public sealed class GraphRule
{
    public GraphRuleKind Kind { get; set; }
    public string Target { get; set; } = "";
    public int Count { get; set; } = 1;

    public static GraphRule Min(string target, int count = 1) => new() { Kind = GraphRuleKind.MinNodes, Target = target, Count = count };
    public static GraphRule Max(string target, int count) => new() { Kind = GraphRuleKind.MaxNodes, Target = target, Count = count };
    public static GraphRule MinInScope(string target, int count = 1) => new() { Kind = GraphRuleKind.MinNodesInScope, Target = target, Count = count };
}

public sealed record GraphRuleResult(GraphRule Rule, bool Passed, int Actual, string Description);

public static class GraphRules
{
    public const string CategoryPrefix = "category:";

    public static bool Matches(string target, NodeDefinition definition)
        => target.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(definition.Category, target[CategoryPrefix.Length..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(definition.TypeId, target, StringComparison.OrdinalIgnoreCase);

    public static string TargetLabel(string target, NodeRegistry? registry = null)
    {
        registry ??= NodeRegistry.Default;
        if (target.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
            return $"блоков из раздела «{NodeCategories.Label(target[CategoryPrefix.Length..])}»";
        return registry.TryGet(target, out var definition) ? $"блоков «{definition.Label}»" : $"блоков «{target}»";
    }

    public static string Describe(GraphRule rule, NodeRegistry? registry = null)
    {
        var target = TargetLabel(rule.Target, registry);
        return rule.Kind switch
        {
            GraphRuleKind.MinNodes => $"Используйте не меньше {rule.Count} {target}",
            GraphRuleKind.MaxNodes when rule.Count == 0 => $"Не используйте {target}",
            GraphRuleKind.MaxNodes => $"Используйте не больше {rule.Count} {target}",
            GraphRuleKind.MinNodesInScope => $"Внутри тела цикла должно быть не меньше {rule.Count} {target}",
            _ => rule.Kind.ToString()
        };
    }

    public static IReadOnlyList<GraphRuleResult> Check(GraphProgram program, IEnumerable<GraphRule> rules, NodeRegistry? registry = null)
    {
        registry ??= NodeRegistry.Default;
        var definitions = new Dictionary<Guid, NodeDefinition>();
        foreach (var node in program.Nodes)
            if (registry.TryGet(node.Type, out var definition))
                definitions.TryAdd(node.Id, definition);

        GraphAnalysis? analysis = null;
        var results = new List<GraphRuleResult>();
        foreach (var rule in rules)
        {
            int actual;
            bool passed;
            switch (rule.Kind)
            {
                case GraphRuleKind.MinNodes:
                    actual = definitions.Values.Count(definition => Matches(rule.Target, definition));
                    passed = actual >= rule.Count;
                    break;
                case GraphRuleKind.MaxNodes:
                    actual = definitions.Values.Count(definition => Matches(rule.Target, definition));
                    passed = actual <= rule.Count;
                    break;
                case GraphRuleKind.MinNodesInScope:
                    var scopes = analysis ??= new GraphAnalysis(program, registry);
                    actual = scopes.ScopeOwners()
                        .Select(owner => scopes.ScopeBody(owner).Count(id => definitions.TryGetValue(id, out var definition) && Matches(rule.Target, definition)))
                        .DefaultIfEmpty(0)
                        .Max();
                    passed = actual >= rule.Count;
                    break;
                default:
                    actual = 0;
                    passed = true;
                    break;
            }
            results.Add(new GraphRuleResult(rule, passed, actual, Describe(rule, registry)));
        }
        return results;
    }
}
