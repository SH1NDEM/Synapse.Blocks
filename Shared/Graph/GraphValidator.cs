using System.Globalization;

namespace Synapse.Blocks.Graph;

public enum IssueSeverity
{
    /// <summary>Программу нельзя запустить.</summary>
    Error,
    /// <summary>Программа запустится, но, скорее всего, собрана не так, как задумано.</summary>
    Warning
}

/// <summary>Замечание к схеме. BlockId задан, если замечание относится к схеме внутри своего блока.</summary>
public sealed record GraphIssue(IssueSeverity Severity, string Message, Guid? NodeId = null, Guid? LinkId = null, string? BlockId = null);

/// <summary>
/// Общие проверки структуры программы. Ничего не знает о конкретных блоках: всё, что ему нужно,
/// берётся из описаний портов и настроек в <see cref="NodeDefinition"/>.
/// </summary>
public static class GraphValidator
{
    public static IReadOnlyList<GraphIssue> Validate(GraphProgram program, NodeRegistry? registry = null)
    {
        registry = CustomBlocks.Resolve(program, registry ?? NodeRegistry.Default);
        var issues = new List<GraphIssue>();
        void Error(string message, Guid? node = null, Guid? link = null) => issues.Add(new(IssueSeverity.Error, message, node, link));
        void Warn(string message, Guid? node = null, Guid? link = null) => issues.Add(new(IssueSeverity.Warning, message, node, link));

        // Блоки.
        var definitions = new Dictionary<Guid, NodeDefinition>();
        foreach (var node in program.Nodes)
        {
            if (definitions.ContainsKey(node.Id))
            {
                Error("Два блока имеют одинаковый идентификатор.", node.Id);
                continue;
            }
            if (!registry.TryGet(node.Type, out var definition))
            {
                Error($"Неизвестный блок «{node.Type}».", node.Id);
                continue;
            }
            definitions[node.Id] = definition;
        }

        foreach (var definition in registry.All)
        {
            var count = definitions.Values.Count(item => item == definition);
            if (definition.MinPerGraph is int min && count < min)
                Error(min == 1 ? $"Нужен блок «{definition.Label}»." : $"Нужно хотя бы {min} блоков «{definition.Label}».");
            if (definition.MaxPerGraph is int max && count > max)
                Error(max == 1 ? $"Блок «{definition.Label}» может быть только один." : $"Блоков «{definition.Label}» может быть не больше {max}.");
        }

        var entries = definitions.Where(pair => pair.Value.IsEntry).Select(pair => pair.Key).ToList();
        if (entries.Count == 0 && !registry.All.Any(definition => definition.IsEntry && definition.MinPerGraph > 0))
            Error("В программе нет блока, с которого начинается выполнение.");
        else if (entries.Count > 1 && !registry.All.Any(definition => definition.IsEntry && definition.MaxPerGraph == 1))
            Error("Выполнение может начинаться только с одного блока.");

        // Переменные.
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in program.Variables)
        {
            var name = (variable.Name ?? "").Trim();
            if (name.Length == 0)
                Error("У переменной не указано имя.");
            else if (!variableNames.Add(name))
                Error($"Переменная «{name}» объявлена дважды.");
        }

        // Настройки блоков.
        foreach (var node in program.Nodes)
        {
            if (!definitions.TryGetValue(node.Id, out var definition)) continue;
            foreach (var param in definition.Params)
            {
                var value = node.Params.TryGetValue(param.Id, out var raw) ? raw : param.Default;
                switch (param.Type)
                {
                    case ParamType.Choice when param.Choices.All(choice => choice.Id != value):
                        Error($"Блок «{definition.Label}»: неизвестный вариант «{value}» в настройке «{param.Label}».", node.Id);
                        break;
                    case ParamType.Variable when string.IsNullOrWhiteSpace(value):
                        Error($"Блок «{definition.Label}»: выберите переменную.", node.Id);
                        break;
                    case ParamType.Variable when !variableNames.Contains(value.Trim()):
                        Error($"Блок «{definition.Label}»: переменная «{value.Trim()}» не объявлена.", node.Id);
                        break;
                    case ParamType.Number when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                        Error($"Блок «{definition.Label}»: в настройке «{param.Label}» должно быть число.", node.Id);
                        break;
                }
            }
            foreach (var message in definition.Validate(node, program))
                Error(message, node.Id);
        }

        // Провода.
        var validLinks = new List<GraphLink>();
        var linkIds = new HashSet<Guid>();
        var execOutputs = new Dictionary<(Guid, string), int>();
        var dataInputs = new Dictionary<(Guid, string), int>();
        foreach (var link in program.Links)
        {
            if (!linkIds.Add(link.Id))
                Error("Два провода имеют одинаковый идентификатор.", null, link.Id);
            if (!definitions.TryGetValue(link.FromNode, out var fromDefinition) || !definitions.TryGetValue(link.ToNode, out var toDefinition))
            {
                Error("Провод ведёт к удалённому или неизвестному блоку.", null, link.Id);
                continue;
            }
            if (link.FromNode == link.ToNode)
            {
                Error($"Блок «{fromDefinition.Label}» нельзя соединить сам с собой.", link.FromNode, link.Id);
                continue;
            }

            var from = fromDefinition.FindPort(link.FromPort);
            var to = toDefinition.FindPort(link.ToPort);
            if (from is null || !from.IsOutput)
            {
                Error($"У блока «{fromDefinition.Label}» нет выхода «{link.FromPort}».", link.FromNode, link.Id);
                continue;
            }
            if (to is null || !to.IsInput)
            {
                Error($"У блока «{toDefinition.Label}» нет входа «{link.ToPort}».", link.ToNode, link.Id);
                continue;
            }
            if (from.Kind != to.Kind)
            {
                Error(from.IsExec
                    ? "Провод исполнения нельзя подключить ко входу данных."
                    : "Провод данных нельзя подключить ко входу исполнения.", link.ToNode, link.Id);
                continue;
            }
            if (from.IsData && !DataTypes.CanConnect(from.Type, to.Type))
            {
                Error($"Вход «{to.DisplayName}» блока «{toDefinition.Label}» ждёт {DataTypes.Label(to.Type)}, а к нему подключено {DataTypes.Label(from.Type)}.", link.ToNode, link.Id);
                continue;
            }

            if (from.IsExec)
            {
                var key = (link.FromNode, link.FromPort);
                execOutputs[key] = execOutputs.GetValueOrDefault(key) + 1;
                if (execOutputs[key] == 2)
                    Error($"Из выхода «{from.DisplayName}» блока «{fromDefinition.Label}» проведено несколько проводов исполнения. Оставьте один.", link.FromNode, link.Id);
            }
            else
            {
                var key = (link.ToNode, link.ToPort);
                dataInputs[key] = dataInputs.GetValueOrDefault(key) + 1;
                if (dataInputs[key] == 2)
                    Error($"Ко входу «{to.DisplayName}» блока «{toDefinition.Label}» подключено несколько проводов. Оставьте один.", link.ToNode, link.Id);
            }
            validLinks.Add(link);
        }

        // Обязательные входы данных.
        var linkedInputs = validLinks.Select(link => (link.ToNode, link.ToPort)).ToHashSet();
        foreach (var node in program.Nodes)
        {
            if (!definitions.TryGetValue(node.Id, out var definition)) continue;
            foreach (var port in definition.Inputs.Where(port => port.IsData && port.InlineDefault is null))
                if (!linkedInputs.Contains((node.Id, port.Id)))
                    Error($"Подключите вход «{port.DisplayName}» блока «{definition.Label}».", node.Id);
        }

        foreach (var nodeId in FindPureCycles(definitions, validLinks))
            Error("Блоки данных замкнуты в круг: значение зависит само от себя.", nodeId);

        // Путь исполнения.
        if (entries.Count == 1)
        {
            var analysis = new GraphAnalysis(program, registry);
            var reachable = analysis.ReachableFrom(entries[0]);
            var insideScopes = analysis.NodesInsideScopes();

            foreach (var (nodeId, definition) in definitions)
                if (!definition.IsPure && !reachable.Contains(nodeId))
                    Warn($"Блок «{definition.Label}» не выполнится: к нему не ведёт провод исполнения.", nodeId);

            bool IsTerminal(NodeDefinition definition) => !definition.IsPure && !definition.Outputs.Any(port => port.IsExec);
            if (definitions.Values.FirstOrDefault(IsTerminal) is { } terminal && !reachable.Any(id => definitions.TryGetValue(id, out var definition) && IsTerminal(definition)))
                Error($"Провода исполнения не доводят программу до блока «{terminal.Label}».");

            var linkedExecOutputs = execOutputs.Keys.ToHashSet();
            foreach (var nodeId in reachable)
            {
                if (!definitions.TryGetValue(nodeId, out var definition)) continue;
                foreach (var port in definition.Outputs.Where(port => port.IsExec && !port.Optional))
                {
                    if (linkedExecOutputs.Contains((nodeId, port.Id))) continue;
                    if (port.OpensScope)
                        Warn($"Ветка «{port.DisplayName}» блока «{definition.Label}» пустая.", nodeId);
                    else if (!insideScopes.Contains(nodeId))
                        Warn($"Выход «{port.DisplayName}» блока «{definition.Label}» никуда не подключён.", nodeId);
                }
            }
        }

        // Свои блоки: проверяются те, что выполнятся при запуске (стоят в программе или внутри таких блоков).
        if (registry.CustomScope is { } scope && registry.BodyOf is null)
            foreach (var block in scope.UsedBy(program))
                issues.AddRange(ValidateBlock(block, scope));

        return issues;
    }

    /// <summary>
    /// Проверяет свой блок: входы и выходы и схему внутри него. Замечания помечены Id блока,
    /// а в тексте названо имя блока, чтобы их можно было показать и снаружи.
    /// </summary>
    public static IReadOnlyList<GraphIssue> ValidateBlock(GraphCustomBlock block, CustomBlockScope scope)
    {
        var issues = new List<GraphIssue>();
        var name = string.IsNullOrWhiteSpace(block.Name) ? "Мой блок" : block.Name.Trim();
        void Add(IssueSeverity severity, string message, Guid? node = null, Guid? link = null)
            => issues.Add(new GraphIssue(severity, $"«{name}»: {message}", node, link, block.Id));

        if (string.IsNullOrWhiteSpace(block.Name))
            Add(IssueSeverity.Error, "дайте блоку название.");
        if (block.Outputs.Count == 0)
            Add(IssueSeverity.Error, "у блока должен быть хотя бы один выход.");
        foreach (var port in block.Inputs.Concat(block.Outputs).Where(port => string.IsNullOrWhiteSpace(port.Name)))
            Add(IssueSeverity.Error, "у каждого входа и выхода должно быть название.");
        if (!scope.IsConnected(block))
        {
            Add(IssueSeverity.Error, "блок не удалось подключить: проверьте, что названия его входов и выходов не повторяются.");
            return issues.DistinctBy(issue => issue.Message).ToList();
        }
        if (scope.DependsOn(block, block))
        {
            Add(IssueSeverity.Error, "блок использует сам себя (прямо или через другие блоки) — такой блок никогда не закончит работу.");
            return issues;
        }

        foreach (var issue in Validate(block.Body, scope.BodyRegistry(block)))
            Add(issue.Severity, issue.Message, issue.NodeId, issue.LinkId);
        return issues.DistinctBy(issue => issue.Message).ToList();
    }

    /// <summary>Чистые блоки, которые через провода данных зависят сами от себя.</summary>
    private static HashSet<Guid> FindPureCycles(Dictionary<Guid, NodeDefinition> definitions, List<GraphLink> links)
    {
        var dependencies = new Dictionary<Guid, List<Guid>>();
        foreach (var link in links)
        {
            if (!definitions[link.FromNode].IsPure || !definitions[link.ToNode].IsPure) continue;
            if (!definitions[link.FromNode].FindPort(link.FromPort)!.IsData) continue;
            if (!dependencies.TryGetValue(link.ToNode, out var list))
                dependencies[link.ToNode] = list = [];
            list.Add(link.FromNode);
        }

        var inCycle = new HashSet<Guid>();
        var state = new Dictionary<Guid, int>(); // 1 — в обходе, 2 — проверен.
        var path = new Stack<Guid>();

        void Visit(Guid node)
        {
            state[node] = 1;
            path.Push(node);
            foreach (var next in dependencies.GetValueOrDefault(node) ?? [])
            {
                var nextState = state.GetValueOrDefault(next);
                if (nextState == 0)
                {
                    Visit(next);
                }
                else if (nextState == 1)
                {
                    // Все блоки пути от next до текущего образуют круг.
                    foreach (var item in path)
                    {
                        inCycle.Add(item);
                        if (item == next) break;
                    }
                }
            }
            path.Pop();
            state[node] = 2;
        }

        foreach (var node in dependencies.Keys.ToList())
            if (state.GetValueOrDefault(node) == 0)
                Visit(node);
        return inCycle;
    }
}

/// <summary>Обход проводов исполнения: достижимость и тела вложенных веток.</summary>
public sealed class GraphAnalysis
{
    private readonly Dictionary<Guid, NodeDefinition> _definitions = [];
    private readonly Dictionary<Guid, List<(PortDef Port, Guid Target)>> _execLinks = [];

    public GraphAnalysis(GraphProgram program, NodeRegistry? registry = null)
    {
        registry ??= NodeRegistry.Default;
        foreach (var node in program.Nodes)
            if (registry.TryGet(node.Type, out var definition))
                _definitions.TryAdd(node.Id, definition);

        foreach (var link in program.Links)
        {
            if (!_definitions.TryGetValue(link.FromNode, out var from) || !_definitions.ContainsKey(link.ToNode)) continue;
            var port = from.FindPort(link.FromPort);
            if (port is null || !port.IsExec || !port.IsOutput) continue;
            if (!_execLinks.TryGetValue(link.FromNode, out var list))
                _execLinks[link.FromNode] = list = [];
            list.Add((port, link.ToNode));
        }
    }

    public HashSet<Guid> ReachableFrom(Guid start) => Walk([start], stopAt: null);

    /// <summary>
    /// Блоки внутри веток, которые открывает <paramref name="owner"/> (например, тело цикла),
    /// включая вложенные ветки. Сам владелец в результат не входит.
    /// </summary>
    public HashSet<Guid> ScopeBody(Guid owner)
    {
        var starts = (_execLinks.GetValueOrDefault(owner) ?? [])
            .Where(item => item.Port.OpensScope)
            .Select(item => item.Target);
        return Walk(starts, stopAt: owner);
    }

    /// <summary>Блоки, открывающие вложенные ветки (циклы).</summary>
    public IEnumerable<Guid> ScopeOwners()
        => _definitions.Where(pair => pair.Value.Outputs.Any(port => port.OpensScope)).Select(pair => pair.Key);

    public HashSet<Guid> NodesInsideScopes()
    {
        var result = new HashSet<Guid>();
        foreach (var owner in ScopeOwners())
            result.UnionWith(ScopeBody(owner));
        return result;
    }

    private HashSet<Guid> Walk(IEnumerable<Guid> starts, Guid? stopAt)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(starts);
        while (queue.TryDequeue(out var current))
        {
            if (current == stopAt || !visited.Add(current)) continue;
            foreach (var (_, target) in _execLinks.GetValueOrDefault(current) ?? [])
                queue.Enqueue(target);
        }
        return visited;
    }
}
