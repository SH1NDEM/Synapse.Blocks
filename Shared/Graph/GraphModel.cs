namespace Synapse.Blocks.Graph;

/// <summary>
/// Программа игрока: экземпляры блоков, провода и переменные. Только данные —
/// поведение блоков описано в <see cref="NodeDefinition"/> и находится по <see cref="GraphNode.Type"/>.
/// </summary>
public sealed class GraphProgram
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<GraphNode> Nodes { get; set; } = [];
    public List<GraphLink> Links { get; set; } = [];
    public List<GraphVariable> Variables { get; set; } = [];

    /// <summary>
    /// Свои блоки игрока: подсхемы, которые снаружи работают как обычные чипы данных.
    /// Хранятся только у основной программы; у тела своего блока список пуст.
    /// </summary>
    public List<GraphCustomBlock> CustomBlocks { get; set; } = [];

    public GraphNode? FindNode(Guid id) => Nodes.FirstOrDefault(node => node.Id == id);

    public GraphCustomBlock? FindBlock(string? id)
        => CustomBlocks.FirstOrDefault(block => string.Equals(block.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class GraphNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Идентификатор определения блока в реестре, например <c>"math"</c> или <c>"loop"</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Настройки блока и значения неподключённых входов данных: ключ — Id параметра или порта,
    /// значение — исходная строка, как её ввёл игрок.
    /// </summary>
    public Dictionary<string, string> Params { get; set; } = [];

    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Провод от выхода одного блока ко входу другого. Вид провода определяется видом портов.</summary>
public sealed class GraphLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNode { get; set; }
    public string FromPort { get; set; } = "";
    public Guid ToNode { get; set; }
    public string ToPort { get; set; } = "";
}

/// <summary>Переменная программы. Объявляется один раз, читается и меняется блоками памяти.</summary>
public sealed class GraphVariable
{
    public string Name { get; set; } = "A";
    public string Initial { get; set; } = "0";
}
