using Synapse.Blocks.Graph.Nodes;

namespace Synapse.Blocks.Graph;

/// <summary>
/// Свой блок игрока: подсхема с входами и выходами. Снаружи он выглядит и работает как обычный
/// чип данных (как «Арифметика»): получает значения на входы и отдаёт результат. Внутри — своя схема
/// от чипа «Параметры» до чипа «Вернуть» со своими переменными; в ней можно использовать циклы и условия.
/// </summary>
public sealed class GraphCustomBlock
{
    /// <summary>TypeId блока в реестре, например <c>my-1a2b3c4d</c>.</summary>
    public string Id { get; set; } = CustomBlocks.NewBlockId();
    public string Name { get; set; } = "Мой блок";
    public string Description { get; set; } = "";
    public List<GraphCustomPort> Inputs { get; set; } = [];
    public List<GraphCustomPort> Outputs { get; set; } = [];
    public GraphProgram Body { get; set; } = new();
}

/// <summary>Вход или выход своего блока. Id не меняется при переименовании — на него ссылаются провода.</summary>
public sealed class GraphCustomPort
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DataType Type { get; set; } = DataType.Any;
}

/// <summary>Создание и правка своих блоков.</summary>
public static class CustomBlocks
{
    public const string TypePrefix = "my-";
    /// <summary>Сколько своих блоков может вызывать друг друга вглубь за один запуск.</summary>
    public const int MaxDepth = 24;

    public static string NewBlockId() => TypePrefix + Guid.NewGuid().ToString("N")[..8];

    public static bool IsCustomType(string? typeId)
        => typeId?.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Служебные чипы тела своего блока: «Параметры» и «Вернуть».</summary>
    public static bool IsServiceType(string? typeId)
        => string.Equals(typeId, CustomParamsNode.Id, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeId, CustomReturnNode.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Новый блок с одним входом и одним выходом. Внутри «Параметры» уже соединены с «Вернуть»
    /// порядком и данными, так что блок сразу рабочий и просто возвращает вход.
    /// </summary>
    public static GraphCustomBlock Create(string name, double x = 0, double y = 0)
    {
        var block = new GraphCustomBlock
        {
            Name = name,
            Inputs = [new GraphCustomPort { Id = "i1", Name = "x", Type = DataType.Any }],
            Outputs = [new GraphCustomPort { Id = "o1", Name = "результат", Type = DataType.Any }],
        };
        var parameters = new GraphNode { Type = CustomParamsNode.Id, X = x, Y = y };
        var result = new GraphNode { Type = CustomReturnNode.Id, X = x + 480, Y = y };
        block.Body.Nodes.Add(parameters);
        block.Body.Nodes.Add(result);
        block.Body.Links.Add(new GraphLink { FromNode = parameters.Id, FromPort = "next", ToNode = result.Id, ToPort = "in" });
        block.Body.Links.Add(new GraphLink { FromNode = parameters.Id, FromPort = "i1", ToNode = result.Id, ToPort = "o1" });
        return block;
    }

    /// <summary>Добавляет вход или выход со свободным Id (<c>i2</c>, <c>o3</c>…).</summary>
    public static GraphCustomPort AddPort(GraphCustomBlock block, bool input, string name, DataType type = DataType.Any)
    {
        var prefix = input ? "i" : "o";
        var used = block.Inputs.Concat(block.Outputs).Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        while (used.Contains(prefix + number)) number++;
        var port = new GraphCustomPort { Id = prefix + number, Name = name, Type = type };
        (input ? block.Inputs : block.Outputs).Add(port);
        return port;
    }

    /// <summary>
    /// Удаляет вход или выход и все провода, которые к нему вели: снаружи (у чипов этого блока
    /// в основной программе и в телах других блоков) и внутри (у «Параметров» и «Вернуть»).
    /// </summary>
    public static void RemovePort(GraphProgram root, GraphCustomBlock block, string portId)
    {
        block.Inputs.RemoveAll(port => port.Id == portId);
        block.Outputs.RemoveAll(port => port.Id == portId);

        foreach (var program in AllPrograms(root))
        {
            var users = program.Nodes.Where(node => string.Equals(node.Type, block.Id, StringComparison.OrdinalIgnoreCase)).Select(node => node.Id).ToHashSet();
            foreach (var node in program.Nodes.Where(node => users.Contains(node.Id)))
                node.Params.Remove(portId);
            program.Links.RemoveAll(link => (users.Contains(link.FromNode) && link.FromPort == portId) || (users.Contains(link.ToNode) && link.ToPort == portId));
        }

        var service = block.Body.Nodes.Where(node => IsServiceType(node.Type)).Select(node => node.Id).ToHashSet();
        block.Body.Links.RemoveAll(link => (service.Contains(link.FromNode) && link.FromPort == portId) || (service.Contains(link.ToNode) && link.ToPort == portId));
    }

    /// <summary>Удаляет свой блок и все его чипы — из основной программы и из тел других блоков.</summary>
    public static void RemoveBlock(GraphProgram root, GraphCustomBlock block)
    {
        root.CustomBlocks.Remove(block);
        foreach (var program in AllPrograms(root))
        {
            var users = program.Nodes.Where(node => string.Equals(node.Type, block.Id, StringComparison.OrdinalIgnoreCase)).Select(node => node.Id).ToHashSet();
            program.Nodes.RemoveAll(node => users.Contains(node.Id));
            program.Links.RemoveAll(link => users.Contains(link.FromNode) || users.Contains(link.ToNode));
        }
    }

    /// <summary>Основная программа и тела всех её своих блоков.</summary>
    public static IEnumerable<GraphProgram> AllPrograms(GraphProgram root)
        => root.CustomBlocks.Select(block => block.Body).Prepend(root);

    /// <summary>Реестр основной программы: встроенные блоки и её свои блоки. Всегда собирается заново.</summary>
    public static NodeRegistry Build(GraphProgram program, NodeRegistry baseRegistry)
    {
        baseRegistry = baseRegistry.CustomScope?.Base ?? baseRegistry;
        return program.CustomBlocks.Count == 0 ? baseRegistry : new CustomBlockScope(baseRegistry, program.CustomBlocks).Outer;
    }

    /// <summary>
    /// Реестр для программы: если переданный реестр уже собран для её своих блоков (или это реестр
    /// тела блока), он и возвращается; иначе собирается новый.
    /// </summary>
    public static NodeRegistry Resolve(GraphProgram program, NodeRegistry registry)
    {
        if (registry.CustomScope is { } scope && (registry.BodyOf is not null || ReferenceEquals(scope.Blocks, program.CustomBlocks)))
            return registry;
        if (registry.CustomScope is null && program.CustomBlocks.Count == 0)
            return registry;
        return Build(program, registry);
    }

    internal static string DefaultInline(DataType type) => type switch
    {
        DataType.Number => "0",
        DataType.Bool => "false",
        DataType.List => "[]",
        DataType.Text => "",
        _ => "0"
    };
}

/// <summary>
/// Свои блоки одной программы, собранные в определения для реестра. Знает, какие блоки
/// используют друг друга, и выдаёт реестры для тел блоков.
/// </summary>
public sealed class CustomBlockScope
{
    private readonly Dictionary<string, CustomNodeDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NodeRegistry> _bodies = new(StringComparer.OrdinalIgnoreCase);

    internal CustomBlockScope(NodeRegistry baseRegistry, List<GraphCustomBlock> blocks)
    {
        Base = baseRegistry;
        Blocks = blocks;
        Outer = baseRegistry.CloneDefinitions();
        Outer.CustomScope = this;
        foreach (var block in blocks)
        {
            if (!CustomBlocks.IsCustomType(block.Id) || _definitions.ContainsKey(block.Id)) continue;
            try
            {
                var definition = new CustomNodeDefinition(block, this);
                Outer.Register(definition);
                _definitions[block.Id] = definition;
            }
            catch (ArgumentException)
            {
                // Блок с неправильными портами не подключается; валидатор объяснит почему.
            }
        }
    }

    /// <summary>Реестр без своих блоков.</summary>
    public NodeRegistry Base { get; }
    /// <summary>Реестр основной программы.</summary>
    public NodeRegistry Outer { get; }
    public List<GraphCustomBlock> Blocks { get; }
    public IEnumerable<CustomNodeDefinition> Definitions => _definitions.Values;

    public bool IsConnected(GraphCustomBlock block) => _definitions.TryGetValue(block.Id, out var definition) && definition.Block == block;

    /// <summary>
    /// Реестр внутри блока: встроенные блоки без «Входа» и «Выхода», служебные «Параметры» и «Вернуть»
    /// этого блока и остальные свои блоки (сам себя блок использовать не может).
    /// </summary>
    public NodeRegistry BodyRegistry(GraphCustomBlock block)
    {
        if (_bodies.TryGetValue(block.Id, out var cached) && cached.BodyOf == block)
            return cached;

        var registry = new NodeRegistry { CustomScope = this, BodyOf = block };
        registry.Register(new CustomParamsNode(block));
        registry.Register(new CustomReturnNode(block));
        foreach (var definition in Base.All)
            if (definition.TypeId is not (StartNode.Id or OutputNode.Id))
                registry.Register(definition);
        foreach (var definition in _definitions.Values)
            if (definition.Block != block)
                registry.Register(definition);
        return _bodies[block.Id] = registry;
    }

    /// <summary>Свои блоки, чьи чипы стоят прямо в теле <paramref name="block"/>.</summary>
    public IEnumerable<GraphCustomBlock> DirectlyUsed(GraphCustomBlock block)
        => block.Body.Nodes
            .Select(node => node.Type)
            .Where(CustomBlocks.IsCustomType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(type => _definitions.GetValueOrDefault(type)?.Block)
            .OfType<GraphCustomBlock>();

    /// <summary>Использует ли <paramref name="block"/> блок <paramref name="target"/> — прямо или через другие блоки.</summary>
    public bool DependsOn(GraphCustomBlock block, GraphCustomBlock target)
    {
        var visited = new HashSet<GraphCustomBlock>();
        var queue = new Queue<GraphCustomBlock>(DirectlyUsed(block));
        while (queue.TryDequeue(out var current))
        {
            if (current == target) return true;
            if (!visited.Add(current)) continue;
            foreach (var next in DirectlyUsed(current))
                queue.Enqueue(next);
        }
        return false;
    }

    /// <summary>Свои блоки, которые выполнятся при запуске программы: стоят в ней или внутри таких блоков.</summary>
    public HashSet<GraphCustomBlock> UsedBy(GraphProgram program)
    {
        var result = new HashSet<GraphCustomBlock>();
        var queue = new Queue<GraphCustomBlock>(program.Nodes
            .Select(node => _definitions.GetValueOrDefault(node.Type)?.Block)
            .OfType<GraphCustomBlock>());
        while (queue.TryDequeue(out var block))
        {
            if (!result.Add(block)) continue;
            foreach (var next in DirectlyUsed(block))
                queue.Enqueue(next);
        }
        return result;
    }
}

/// <summary>Определение своего блока для реестра: порты строятся из входов и выходов блока.</summary>
public sealed class CustomNodeDefinition : NodeDefinition
{
    private readonly CustomBlockScope _scope;

    internal CustomNodeDefinition(GraphCustomBlock block, CustomBlockScope scope)
    {
        Block = block;
        _scope = scope;
        Ports =
        [
            .. block.Inputs.Select(port => Port.Inline(port.Id, port.Name, port.Type, CustomBlocks.DefaultInline(port.Type))),
            .. block.Outputs.Select(port => Port.DataOut(port.Id, port.Name, port.Type)),
        ];
    }

    public GraphCustomBlock Block { get; }

    public override string TypeId => Block.Id;
    public override string Label => string.IsNullOrWhiteSpace(Block.Name) ? "Мой блок" : Block.Name.Trim();
    public override string Category => NodeCategories.Custom;
    public override string Description => string.IsNullOrWhiteSpace(Block.Description)
        ? "Свой блок: схема внутри него получает значения входов и возвращает результат. Откройте его, чтобы изменить."
        : Block.Description.Trim();
    public override IReadOnlyList<PortDef> Ports { get; }

    private sealed class CallCache
    {
        public string Key = "";
        public Dictionary<string, Value> Outputs = [];
    }

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var arguments = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var port in Inputs)
            arguments[port.Id] = context.Input(port.Id);

        // Схема внутри блока зависит только от входов, поэтому при тех же входах её не нужно
        // запускать снова — например, когда читают второй выход того же блока.
        var key = string.Join('\u001f', arguments.Select(pair => $"{pair.Key}={pair.Value.Kind}:{pair.Value}"));
        if (context.GetState<CallCache>() is not { } cache || cache.Key != key)
        {
            cache = new CallCache { Key = key, Outputs = context.RunCustomBlock(Block, _scope.BodyRegistry(Block), arguments, Label) };
            context.SetState(cache);
        }

        if (cache.Outputs.TryGetValue(outputPort, out var value))
            return value;
        throw context.Error($"Блок «{Label}» не вернул значение «{FindPort(outputPort)?.DisplayName ?? outputPort}».");
    }
}
