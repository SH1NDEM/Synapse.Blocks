using Synapse.Blocks.Models;

namespace Synapse.Blocks.Graph;

/// <summary>
/// Снимок одного шага исполнения для пошаговой отладки.
/// Inputs и Outputs — значения портов выполненного блока по Id порта; Variables — переменные после шага.
/// </summary>
public sealed record GraphStep(
    Guid NodeId,
    string NodeType,
    string Detail,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyDictionary<string, string> Variables);

public sealed record GraphExecution(
    bool Success,
    Value Output,
    string Error,
    Guid? ErrorNodeId,
    IReadOnlyList<Guid> Visited,
    IReadOnlyList<GraphStep> Steps,
    IReadOnlyList<GraphIssue> Issues)
{
    public string OutputText => Success ? Output.ToString() : "";
}

public sealed record GraphTestResult(
    Guid TestId,
    string Name,
    bool Passed,
    string Input,
    string Expected,
    string Actual,
    string Error,
    Guid? ErrorNodeId,
    IReadOnlyList<Guid> Visited);

/// <summary>
/// Выполняет программу из двух видов проводов. Ничего не знает о конкретных блоках:
/// идёт по проводам исполнения, вызывает <see cref="NodeDefinition.Execute"/> и по требованию
/// вычисляет входы данных, поднимаясь по проводам данных к источникам.
/// </summary>
public sealed class GraphRunner(NodeRegistry? registry = null, int maxSteps = 10_000)
{
    public const int DefaultMaxSteps = 10_000;

    public NodeRegistry Registry { get; } = registry ?? NodeRegistry.Default;

    public GraphExecution Run(GraphProgram program, string rawInput) => Run(program, Value.Parse(rawInput));

    public GraphExecution Run(GraphProgram program, Value input) => Run(Prepare(program), input);

    /// <summary>
    /// Готовит программу к нескольким запускам: собирает реестр со своими блоками и проверяет схему
    /// один раз. Запуски подготовленной программы не повторяют эту работу — это важно для тестов
    /// уровня и игр, где одна и та же схема запускается много раз подряд.
    /// Программу нельзя менять, пока используется подготовленная версия.
    /// </summary>
    public PreparedGraph Prepare(GraphProgram program)
    {
        var registry = CustomBlocks.Resolve(program, Registry);
        var issues = GraphValidator.Validate(program, registry);
        return new PreparedGraph(program, registry, issues);
    }

    /// <param name="recordSteps">Записывать шаги для пошагового показа. Играм они не нужны, и без них запуск быстрее.</param>
    public GraphExecution Run(PreparedGraph prepared, Value input, bool recordSteps = true)
    {
        if (prepared.FirstError is { } error)
            return new GraphExecution(false, Value.Null, error.Message, error.NodeId, [], [], prepared.Issues);
        return new GraphRuntime(prepared.Program, prepared.Registry, input, new RunBudget(maxSteps), depth: 0, arguments: null, recordSteps)
            .Run(prepared.Issues);
    }

    public GraphExecution Run(PreparedGraph prepared, string rawInput, bool recordSteps = true)
        => Run(prepared, Value.Parse(rawInput), recordSteps);

    public IReadOnlyList<GraphTestResult> RunTests(GraphProgram program, IEnumerable<LevelTestCase> tests)
    {
        var prepared = Prepare(program);
        return tests.Select(test => ToResult(test, Run(prepared, test.Input))).ToList();
    }

    /// <summary>Итог теста по уже выполненному запуску.</summary>
    public static GraphTestResult ToResult(LevelTestCase test, GraphExecution execution) => new(
        test.Id,
        test.Name,
        execution.Success && Value.MatchesExpected(execution.Output, test.ExpectedOutput),
        test.Input,
        test.ExpectedOutput,
        execution.OutputText,
        execution.Error,
        execution.ErrorNodeId,
        execution.Visited);
}

/// <summary>Программа, проверенная и готовая к многократному запуску (см. <see cref="GraphRunner.Prepare"/>).</summary>
public sealed class PreparedGraph(GraphProgram program, NodeRegistry registry, IReadOnlyList<GraphIssue> issues)
{
    public GraphProgram Program { get; } = program;
    public NodeRegistry Registry { get; } = registry;
    public IReadOnlyList<GraphIssue> Issues { get; } = issues;
    public GraphIssue? FirstError { get; } = issues.FirstOrDefault(issue => issue.Severity == IssueSeverity.Error);
}

/// <summary>Общий на весь запуск запас шагов: схемы своих блоков тратят его вместе с основной программой.</summary>
internal sealed class RunBudget(int max)
{
    public int Max { get; } = max;
    public int Remaining { get; set; } = max;
}

/// <summary>Состояние одного запуска программы (или схемы своего блока внутри неё).</summary>
internal sealed class GraphRuntime
{
    private readonly RunBudget _budget;
    private readonly int _depth;
    private readonly IReadOnlyDictionary<string, Value> _arguments;
    // Записывать ли шаги для пошагового показа: значения портов и снимки переменных.
    private readonly bool _record;
    private readonly Dictionary<Guid, (GraphNode Node, NodeDefinition Definition)> _nodes = [];
    // Провода исполнения ищутся по выходу, провода данных — по входу: так их обходит интерпретатор.
    private readonly Dictionary<(Guid Node, string Port), GraphLink> _execByOutput = [];
    private readonly Dictionary<(Guid Node, string Port), GraphLink> _dataByInput = [];
    private readonly Dictionary<(Guid Node, string Port), Value> _outputs = [];
    private readonly Dictionary<(Guid Node, string Port), Value> _pureCache = [];
    private readonly HashSet<Guid> _evaluating = [];
    private readonly Dictionary<Guid, object> _states = [];
    private readonly Dictionary<string, Value> _variables = new(StringComparer.OrdinalIgnoreCase);
    // Блоки, чьи вложенные ветки сейчас выполняются (тела циклов). Последний — самый внутренний.
    private readonly List<Guid> _scopes = [];
    private Dictionary<string, string> _stepInputs = [];
    private Dictionary<string, string> _stepOutputs = [];

    public GraphRuntime(GraphProgram program, NodeRegistry registry, Value input, RunBudget budget, int depth, IReadOnlyDictionary<string, Value>? arguments, bool record = true)
    {
        _record = record;
        ProgramInput = input;
        _budget = budget;
        _depth = depth;
        _arguments = arguments ?? new Dictionary<string, Value>();

        foreach (var node in program.Nodes)
            if (registry.TryGet(node.Type, out var definition))
                _nodes[node.Id] = (node, definition);

        foreach (var link in program.Links)
        {
            if (!_nodes.TryGetValue(link.FromNode, out var from)) continue;
            var port = from.Definition.FindPort(link.FromPort);
            if (port is null) continue;
            if (port.IsExec)
                _execByOutput[(link.FromNode, link.FromPort)] = link;
            else
                _dataByInput[(link.ToNode, link.ToPort)] = link;
        }

        foreach (var variable in program.Variables)
            _variables[variable.Name.Trim()] = Value.Parse(variable.Initial);
    }

    public Value ProgramInput { get; }

    /// <summary>Значения, которые схема своего блока вернула через «Вернуть».</summary>
    public Dictionary<string, Value> Returns { get; } = new(StringComparer.Ordinal);

    public Value Argument(string portId, Guid nodeId)
        => _arguments.TryGetValue(portId, out var value)
            ? value
            : throw new GraphRuntimeException($"Блок не получил значение входа «{portId}».", nodeId);

    /// <summary>Запускает схему своего блока с заданными входами и возвращает его выходы.</summary>
    public Dictionary<string, Value> RunCustomBlock(GraphCustomBlock block, NodeRegistry registry, IReadOnlyDictionary<string, Value> arguments, string label, Guid callerId)
    {
        if (_depth + 1 > CustomBlocks.MaxDepth)
            throw new GraphRuntimeException($"Свои блоки вызывают друг друга слишком глубоко (больше {CustomBlocks.MaxDepth} уровней).", callerId);

        // Входные данные программы видны и внутри блока: так блоки игр (например, «Поезд») работают и в своих блоках.
        // Шаги внутри блока никто не показывает — их не записываем.
        var inner = new GraphRuntime(block.Body, registry, ProgramInput, _budget, _depth + 1, arguments, record: false);
        var execution = inner.Run([]);
        if (!execution.Success)
            throw new GraphRuntimeException($"Внутри блока «{label}»: {execution.Error}", callerId);
        return inner.Returns;
    }

    public GraphExecution Run(IReadOnlyList<GraphIssue> issues)
    {
        var visited = new List<Guid>();
        var steps = new List<GraphStep>();
        var entry = _nodes.Values.First(item => item.Definition.IsEntry);
        var current = entry.Node;

        while (_budget.Remaining-- > 0)
        {
            var definition = _nodes[current.Id].Definition;
            visited.Add(current.Id);

            // Управление вернулось в блок, открывший ветку: закрываем её и всё, что вложено.
            var scopeIndex = _scopes.LastIndexOf(current.Id);
            if (scopeIndex >= 0)
                _scopes.RemoveRange(scopeIndex, _scopes.Count - scopeIndex);

            _pureCache.Clear();
            if (_record)
            {
                _stepInputs = [];
                _stepOutputs = [];
            }

            ExecResult result;
            try
            {
                result = definition.Execute(new NodeContext(this, current, definition, executing: true));
            }
            catch (GraphRuntimeException exception)
            {
                if (Snapshot(current, exception.Message) is { } failedStep) steps.Add(failedStep);
                return Fail(exception.Message, exception.NodeId ?? current.Id);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var message = $"Внутренняя ошибка блока «{definition.Label}»: {exception.Message}";
                if (Snapshot(current, message) is { } crashedStep) steps.Add(crashedStep);
                return Fail(message, current.Id);
            }

            if (Snapshot(current, result.Detail) is { } doneStep) steps.Add(doneStep);

            if (result.Action == ExecAction.Finish)
                return new GraphExecution(true, result.Result, "", null, visited, steps, issues);

            var port = definition.FindPort(result.Port);
            if (port is null || !port.IsExec || !port.IsOutput)
                return Fail($"Блок «{definition.Label}» выбрал несуществующий выход «{result.Port}».", current.Id);

            if (result.Action == ExecAction.EnterScope)
            {
                _scopes.Add(current.Id);
                // Пустое тело сразу возвращает управление в блок.
                if (_execByOutput.TryGetValue((current.Id, port.Id), out var bodyLink))
                    current = _nodes[bodyLink.ToNode].Node;
                continue;
            }

            if (_execByOutput.TryGetValue((current.Id, port.Id), out var link))
            {
                current = _nodes[link.ToNode].Node;
            }
            else if (_scopes.Count > 0)
            {
                // Ветка закончилась внутри тела цикла — возвращаемся к циклу.
                current = _nodes[_scopes[^1]].Node;
            }
            else
            {
                return Fail(
                    $"Программа остановилась на блоке «{definition.Label}»: выход «{port.DisplayName}» никуда не подключён, а блок «Выход» не достигнут.",
                    current.Id);
            }
        }

        return Fail($"Программа превысила безопасный предел в {_budget.Max} шагов. Возможно, цикл никогда не заканчивается.", current.Id);

        GraphExecution Fail(string error, Guid? nodeId)
            => new(false, Value.Null, error, nodeId, visited, steps, issues);
    }

    private GraphStep? Snapshot(GraphNode node, string detail) => !_record ? null : new(
        node.Id,
        node.Type,
        detail,
        _stepInputs,
        _stepOutputs,
        _variables.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase));

    public Value ReadInput(GraphNode node, NodeDefinition definition, string portId, bool record)
    {
        var port = definition.FindPort(portId);
        if (port is null || !port.IsData || !port.IsInput)
            throw new GraphRuntimeException($"У блока «{definition.Label}» нет входа «{portId}».", node.Id);

        Value raw;
        if (_dataByInput.TryGetValue((node.Id, portId), out var link))
            raw = ReadOutput(link.FromNode, link.FromPort);
        else if (port.InlineDefault is not null)
            raw = Value.Parse(node.Params.TryGetValue(portId, out var inline) ? inline : port.InlineDefault);
        else
            throw new GraphRuntimeException($"Вход «{port.DisplayName}» блока «{definition.Label}» не подключён.", node.Id);

        if (!DataTypes.TryCoerce(raw, port.Type, out var value, out var error))
            throw new GraphRuntimeException($"Блок «{definition.Label}», вход «{port.DisplayName}»: {error}.", node.Id);

        if (record && _record)
            _stepInputs[port.Id] = value.ToString();
        return value;
    }

    private Value ReadOutput(Guid nodeId, string portId)
    {
        var (node, definition) = _nodes[nodeId];
        var port = definition.FindPort(portId);
        var portName = port?.DisplayName ?? portId;

        if (definition.IsPure)
        {
            // Чистый блок пересчитывается на каждом шаге: его входы могли измениться.
            if (_pureCache.TryGetValue((nodeId, portId), out var cached))
                return cached;
            if (!_evaluating.Add(nodeId))
                throw new GraphRuntimeException($"Блок «{definition.Label}» зависит сам от себя через провода данных.", nodeId);
            try
            {
                var value = definition.Evaluate(new NodeContext(this, node, definition, executing: false), portId);
                _pureCache[(nodeId, portId)] = value;
                return value;
            }
            catch (GraphRuntimeException exception) when (exception.NodeId is null)
            {
                exception.NodeId = nodeId;
                throw;
            }
            finally
            {
                _evaluating.Remove(nodeId);
            }
        }

        // Выход блока исполнения хранит значение с момента его последнего выполнения.
        if (_outputs.TryGetValue((nodeId, portId), out var stored))
            return stored;
        throw new GraphRuntimeException(
            $"Блок «{definition.Label}» ещё не выполнялся, поэтому его выход «{portName}» пока пуст. Проведите провод исполнения так, чтобы этот блок выполнился раньше.",
            nodeId);
    }

    public void SetOutput(GraphNode node, NodeDefinition definition, string portId, Value value, bool record)
    {
        var port = definition.FindPort(portId);
        if (port is null || !port.IsData || !port.IsOutput)
            throw new GraphRuntimeException($"У блока «{definition.Label}» нет выхода «{portId}».", node.Id);
        _outputs[(node.Id, portId)] = value;
        if (record && _record)
            _stepOutputs[port.Id] = value.ToString();
    }

    public object? GetState(Guid nodeId) => _states.GetValueOrDefault(nodeId);

    public void SetState(Guid nodeId, object? state)
    {
        if (state is null)
            _states.Remove(nodeId);
        else
            _states[nodeId] = state;
    }

    public Value GetVariable(string name, Guid nodeId)
    {
        if (_variables.TryGetValue(name.Trim(), out var value))
            return value;
        throw new GraphRuntimeException($"Переменная «{name}» не объявлена.", nodeId);
    }

    public void SetVariable(string name, Value value, Guid nodeId)
    {
        var key = name.Trim();
        if (!_variables.ContainsKey(key))
            throw new GraphRuntimeException($"Переменная «{name}» не объявлена.", nodeId);
        _variables[key] = value;
    }
}

/// <summary>То, что видит блок во время выполнения: свои входы, выходы, настройки, состояние и переменные.</summary>
public sealed class NodeContext
{
    private readonly GraphRuntime _runtime;
    private readonly bool _executing;

    internal NodeContext(GraphRuntime runtime, GraphNode node, NodeDefinition definition, bool executing)
    {
        _runtime = runtime;
        _executing = executing;
        Node = node;
        Definition = definition;
    }

    public GraphNode Node { get; }
    public NodeDefinition Definition { get; }

    /// <summary>Входные данные текущего теста.</summary>
    public Value ProgramInput => _runtime.ProgramInput;

    /// <summary>Значение входа данных, уже приведённое к типу порта.</summary>
    public Value Input(string portId) => _runtime.ReadInput(Node, Definition, portId, _executing);

    public double Number(string portId)
    {
        var value = Input(portId);
        if (value.TryGetNumber(out var number))
            return number;
        throw Error($"Вход «{PortName(portId)}»: ожидалось число, а пришло «{value}».");
    }

    public string Text(string portId) => Input(portId).ToString();

    public bool Bool(string portId)
    {
        var value = Input(portId);
        if (value.TryGetBool(out var boolean))
            return boolean;
        throw Error($"Вход «{PortName(portId)}»: ожидалось логическое значение, а пришло «{value}».");
    }

    /// <summary>Настройка блока или её значение по умолчанию.</summary>
    public string Param(string id)
        => Node.Params.TryGetValue(id, out var value) ? value : Definition.FindParam(id)?.Default ?? "";

    public void SetOutput(string portId, Value value)
        => _runtime.SetOutput(Node, Definition, portId, value, _executing);

    /// <summary>Состояние, которое блок хранит между своими выполнениями (например, счётчик цикла).</summary>
    public T? GetState<T>() where T : class => _runtime.GetState(Node.Id) as T;

    public void SetState(object? state) => _runtime.SetState(Node.Id, state);

    public Value GetVariable(string name) => _runtime.GetVariable(name, Node.Id);

    public void SetVariable(string name, Value value) => _runtime.SetVariable(name, value, Node.Id);

    /// <summary>Значение входа своего блока — для чипа «Параметры».</summary>
    public Value Argument(string portId) => _runtime.Argument(portId, Node.Id);

    /// <summary>Выход своего блока — для чипа «Вернуть».</summary>
    public void Return(string portId, Value value) => _runtime.Returns[portId] = value;

    /// <summary>Запускает схему своего блока и возвращает значения его выходов.</summary>
    internal Dictionary<string, Value> RunCustomBlock(GraphCustomBlock block, NodeRegistry registry, IReadOnlyDictionary<string, Value> arguments, string label)
        => _runtime.RunCustomBlock(block, registry, arguments, label, Node.Id);

    /// <summary>Создаёт ошибку для игрока, привязанную к этому блоку: <c>throw context.Error("...")</c>.</summary>
    public GraphRuntimeException Error(string message) => new(message, Node.Id);

    private string PortName(string portId) => Definition.FindPort(portId)?.DisplayName ?? portId;
}
