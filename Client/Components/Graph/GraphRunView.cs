using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Components.Graph;

/// <summary>
/// Что показывает поле во время запуска: текущий и пройденные блоки, блок с ошибкой,
/// значения на портах и переменные. Заполняется страницей игры по шагам <see cref="GraphStep"/>.
/// </summary>
public sealed class GraphRunView
{
    public const string Mask = "•••";

    /// <summary>
    /// Номер изменения: растёт при каждом изменении показа. По нему поле понимает,
    /// что подсветку нужно перерисовать, и не перерисовывается зря.
    /// </summary>
    public int Version { get; private set; }

    public Guid? ActiveNode { get; private set; }
    public Guid? PreviousNode { get; private set; }

    private Guid? _errorNode;
    public Guid? ErrorNode
    {
        get => _errorNode;
        set { _errorNode = value; Version++; }
    }
    public HashSet<Guid> Executed { get; } = [];
    public Dictionary<(Guid Node, string Port), string> PortValues { get; } = [];
    public Dictionary<string, string> Variables { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ожидаемый ответ текущего теста — показывается над блоком «Выход».</summary>
    private string? _expected;
    public string? Expected
    {
        get => _expected;
        set { _expected = value; Version++; }
    }

    public bool IsEmpty => ActiveNode is null && ErrorNode is null && Executed.Count == 0 && PortValues.Count == 0 && Expected is null;

    public void Clear()
    {
        if (IsEmpty && Variables.Count == 0) return;
        ActiveNode = null;
        PreviousNode = null;
        _errorNode = null;
        Executed.Clear();
        PortValues.Clear();
        Variables = new(StringComparer.OrdinalIgnoreCase);
        _expected = null;
        Version++;
    }

    /// <summary>Переходит к шагу: подсвечивает блок и запоминает значения его портов.</summary>
    public void Apply(GraphStep step, bool masked)
    {
        if (ActiveNode is Guid active)
            Executed.Add(active);
        PreviousNode = ActiveNode;
        ActiveNode = step.NodeId;
        foreach (var (port, value) in step.Inputs)
            PortValues[(step.NodeId, port)] = masked ? Mask : value;
        foreach (var (port, value) in step.Outputs)
            PortValues[(step.NodeId, port)] = masked ? Mask : value;
        Variables = step.Variables.ToDictionary(
            pair => pair.Key,
            pair => masked ? Mask : pair.Value,
            StringComparer.OrdinalIgnoreCase);
        Version++;
    }

    /// <summary>Завершает показ: текущий блок становится пройденным.</summary>
    public void Finish()
    {
        if (ActiveNode is Guid active)
            Executed.Add(active);
        ActiveNode = null;
        PreviousNode = null;
        Version++;
    }

    public string? ValueOf(Guid node, string port) => PortValues.TryGetValue((node, port), out var value) ? value : null;
}
