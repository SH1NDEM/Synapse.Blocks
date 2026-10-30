using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Games.Network;

/// <summary>Что стоит в узле сети — от этого зависит значок на карте.</summary>
public enum NetNodeKind
{
    /// <summary>Район города.</summary>
    District,
    /// <summary>Телефонная станция (АТС) — узел, через который идут каналы районов.</summary>
    Exchange,
    /// <summary>Радиовышка или ретранслятор.</summary>
    Tower,
    /// <summary>Дом или квартал.</summary>
    House
}

/// <summary>Узел сети. X и Y — положение на карте (поле 1000 × 340).</summary>
public sealed record NetNode(string Id, double X, double Y, NetNodeKind Kind = NetNodeKind.District);

/// <summary>
/// Деталь городской карты под сетью: река, квартал, парк, улица. Path — SVG-путь в координатах карты,
/// Kind — класс оформления (river, block, park, road), Label — подпись в точке (LabelX, LabelY).
/// </summary>
public sealed record NetDecor(string Kind, string Path, string Label = "", double LabelX = 0, double LabelY = 0);

/// <summary>Канал связи между двумя узлами. Оборванный канал виден на карте, но данные по нему не идут.</summary>
public sealed record NetLink(string A, string B, bool Broken = false)
{
    public bool Connects(string a, string b)
        => (Same(A, a) && Same(B, b)) || (Same(A, b) && Same(B, a));

    public string? Other(string node) => Same(A, node) ? B : Same(B, node) ? A : null;

    internal static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Пакет данных, который нужно доставить из From в To.</summary>
public sealed record NetPacket(int Number, string From, string To);

/// <summary>
/// Задание игры «Связь». Схема игрока — маршрутизатор: пакет стоит в узле, и схема отвечает,
/// в какой соседний узел передать его дальше. Так шаг за шагом пакет идёт до цели.
/// </summary>
public sealed class NetworkLevel
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Objective { get; init; } = "";
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public IReadOnlyList<NetNode> Nodes { get; init; } = [];
    public IReadOnlyList<NetLink> Links { get; init; } = [];
    public IReadOnlyList<NetPacket> Packets { get; init; } = [];
    public IReadOnlyList<NetDecor> Decor { get; init; } = [];
    public IReadOnlyList<string> AllowedNodes { get; init; } = [];
    /// <summary>Сколько передач может сделать пакет, прежде чем считается потерянным.</summary>
    public int MaxHops { get; init; } = 12;

    /// <summary>Рабочие соседи узла в порядке описания каналов.</summary>
    public IReadOnlyList<string> Neighbors(string node)
        => Links.Where(link => !link.Broken).Select(link => link.Other(node)).OfType<string>().ToList();

    public LevelDefinition AsLevel() => new()
    {
        Id = Id,
        Order = Order,
        Title = Title,
        Objective = Objective,
        Description = Description,
        Hint = Hint,
        Direction = "Связь",
        AllowedNodes = [.. LevelNodes.Mandatory, .. AllowedNodes],
    };
}

/// <summary>Как прошёл один пакет: узлы маршрута по порядку и время старта.</summary>
public sealed record NetworkPacketResult(NetPacket Packet, IReadOnlyList<string> Path, string Error, double Start)
{
    public bool Passed => Error.Length == 0;
    /// <summary>Сколько длится доставка (или путь до ошибки) на экране.</summary>
    public double End => Start + Math.Max(1, Path.Count - 1) * NetworkSimulator.HopTime + (Passed ? .4 : 1.2);
}

public sealed record NetworkRun(bool Success, string Error, IReadOnlyList<NetworkPacketResult> Packets, double Duration);

/// <summary>
/// Прогон пакетов. Каждый пакет идёт отдельно: в каждом узле запускается схема игрока, её ответ —
/// следующий узел. Он должен быть соседом по рабочему каналу. На первой ошибке прогон останавливается.
/// </summary>
public static class NetworkSimulator
{
    /// <summary>Секунды на одну передачу между узлами и пауза между пакетами.</summary>
    public const double HopTime = 1;
    public const double PacketGap = .6;

    /// <summary>Входные данные схемы — список: номер пакета, текущий узел, цель, соседи, откуда пришёл, шаг.</summary>
    public static Value Input(NetPacket packet, string current, IReadOnlyList<string> neighbors, string previous, int step) => Value.FromList(
    [
        Value.FromNumber(packet.Number),
        NodeValue(current),
        NodeValue(packet.To),
        Value.FromList(neighbors.Select(NodeValue)),
        previous.Length == 0 ? Value.FromText("") : NodeValue(previous),
        Value.FromNumber(step),
    ]);

    /// <summary>Номера узлов («3») отдаются числами, названия — текстом.</summary>
    private static Value NodeValue(string id) => Value.Parse(id) is { IsNumber: true } number ? number : Value.FromText(id);

    public static NetworkRun Run(NetworkLevel level, GraphProgram program, GraphRunner runner)
    {
        // Схема проверяется один раз на весь прогон, а не перед каждым запуском.
        var prepared = runner.Prepare(program);
        var results = new List<NetworkPacketResult>();
        var time = 0.0;

        foreach (var packet in level.Packets)
        {
            var path = new List<string> { packet.From };
            var previous = "";
            var current = packet.From;
            var error = "";

            while (!NetLink.Same(current, packet.To))
            {
                if (path.Count > level.MaxHops)
                {
                    error = $"пакет сделал {level.MaxHops} передач и так и не дошёл — похоже, он ходит по кругу.";
                    break;
                }

                var neighbors = level.Neighbors(current);
                var execution = runner.Run(prepared, Input(packet, current, neighbors, previous, path.Count - 1), recordSteps: false);
                if (!execution.Success)
                {
                    error = $"в узле «{current}» схема остановилась с ошибкой: {execution.Error}";
                    break;
                }

                var answer = execution.Output.ToString().Trim();
                var next = level.Nodes.FirstOrDefault(node => NetLink.Same(node.Id, answer))?.Id;
                var link = next is null ? null : level.Links.FirstOrDefault(item => item.Connects(current, next));
                if (next is null)
                    error = $"в узле «{current}» схема ответила «{answer}», а такого узла в сети нет.";
                else if (NetLink.Same(next, current))
                    error = $"в узле «{current}» схема ответила тем же узлом — пакет никуда не ушёл.";
                else if (link is null)
                    error = $"между «{current}» и «{next}» нет канала. Соседи «{current}»: {string.Join(", ", neighbors)}.";
                else if (link.Broken)
                    error = $"канал «{current}» — «{next}» оборван, по нему данные не пройдут.";
                if (error.Length > 0)
                    break;

                previous = current;
                current = next!;
                path.Add(current);
            }

            var result = new NetworkPacketResult(packet, path, error, time);
            results.Add(result);
            if (!result.Passed)
                return new NetworkRun(false, $"Пакет №{packet.Number} ({packet.From} → {packet.To}): {error}", results, result.End);
            time = result.End + PacketGap;
        }

        return new NetworkRun(true, "", results, results.Count == 0 ? 0 : results[^1].End);
    }
}
