using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synapse.Blocks.Graph;

/// <summary>
/// Сборка программ из кода: для тестов, шаблонов уровней и примеров в справке.
/// <code>
/// var b = new GraphBuilder();
/// var start = b.Node("start");
/// var add = b.Node("math", ("op", "+"), ("b", "2"));
/// var output = b.Node("output");
/// b.Exec(start, output).Data(start, "value", add, "a").Data(add, "result", output, "value");
/// </code>
/// </summary>
public sealed class GraphBuilder
{
    private double _nextX;

    public GraphProgram Program { get; } = new();

    public GraphNode Node(string type, params (string Key, string Value)[] parameters)
    {
        var node = new GraphNode { Type = type, X = _nextX, Y = 0 };
        _nextX += 260;
        foreach (var (key, value) in parameters)
            node.Params[key] = value;
        Program.Nodes.Add(node);
        return node;
    }

    /// <summary>Провод исполнения. Порты по умолчанию — «next» → «in».</summary>
    public GraphBuilder Exec(GraphNode from, GraphNode to, string fromPort = "next", string toPort = "in")
        => Link(from, fromPort, to, toPort);

    /// <summary>Провод исполнения из именованного выхода (ветки условия, тела цикла).</summary>
    public GraphBuilder Exec(GraphNode from, string fromPort, GraphNode to, string toPort = "in")
        => Link(from, fromPort, to, toPort);

    public GraphBuilder Data(GraphNode from, string fromPort, GraphNode to, string toPort)
        => Link(from, fromPort, to, toPort);

    public GraphBuilder Variable(string name, string initial = "0")
    {
        Program.Variables.Add(new GraphVariable { Name = name, Initial = initial });
        return this;
    }

    private GraphBuilder Link(GraphNode from, string fromPort, GraphNode to, string toPort)
    {
        Program.Links.Add(new GraphLink { FromNode = from.Id, FromPort = fromPort, ToNode = to.Id, ToPort = toPort });
        return this;
    }
}

/// <summary>Сериализация программ без reflection — работает и после тримминга WebAssembly.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GraphProgram))]
[JsonSerializable(typeof(List<GraphRule>))]
[JsonSerializable(typeof(List<GraphCustomBlock>))]
public partial class GraphJsonContext : JsonSerializerContext;

public static class GraphJson
{
    public static string Serialize(GraphProgram program)
        => JsonSerializer.Serialize(program, GraphJsonContext.Default.GraphProgram);

    public static GraphProgram Deserialize(string json)
        => JsonSerializer.Deserialize(json, GraphJsonContext.Default.GraphProgram) ?? new GraphProgram();

    public static string SerializeBlocks(List<GraphCustomBlock> blocks)
        => JsonSerializer.Serialize(blocks, GraphJsonContext.Default.ListGraphCustomBlock);

    public static List<GraphCustomBlock> DeserializeBlocks(string json)
        => JsonSerializer.Deserialize(json, GraphJsonContext.Default.ListGraphCustomBlock) ?? [];

    /// <summary>Глубокая копия своего блока (для библиотеки и импорта).</summary>
    public static GraphCustomBlock Clone(GraphCustomBlock block)
        => DeserializeBlocks(SerializeBlocks([block]))[0];
}
