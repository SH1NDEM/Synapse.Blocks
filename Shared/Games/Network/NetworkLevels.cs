using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Games.Network;

/// <summary>Блок «Пакет»: где сейчас пакет, куда ему нужно и какие соседи доступны.</summary>
public sealed class PacketNode : NodeDefinition
{
    public const string Id = "packet";

    public override string TypeId => Id;
    public override string Label => "Пакет";
    public override string Category => NodeCategories.Game;
    public override string Description => "Пакет данных в узле сети: номер, текущий узел, цель, соседи по рабочим каналам, узел, откуда пакет пришёл, и номер шага.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataOut("number", "номер", DataType.Number),
        Port.DataOut("current", "здесь"),
        Port.DataOut("target", "цель"),
        Port.DataOut("neighbors", "соседи", DataType.List),
        Port.DataOut("previous", "откуда"),
        Port.DataOut("step", "шаг", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.ProgramInput.Items;
        if (items.Length < 6)
            throw context.Error("Блок «Пакет» работает только в игре «Связь».");
        return outputPort switch
        {
            "number" => items[0],
            "current" => items[1],
            "target" => items[2],
            "neighbors" => items[3],
            "previous" => items[4],
            _ => items[5]
        };
    }
}

public static class NetworkNodes
{
    /// <summary>Встроенные блоки и блок «Пакет».</summary>
    public static NodeRegistry Registry { get; } = Create();

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.Default.CloneDefinitions();
        registry.Register(new PacketNode());
        return registry;
    }
}

/// <summary>Задания связи: от прямого канала до обхода оборванного кольца.</summary>
public static class NetworkLevels
{
    private static NetNode[] Star(string hub) =>
    [
        new(hub, 500, 170),
        new("Север", 500, 46),
        new("Восток", 790, 170),
        new("Юг", 500, 294),
        new("Запад", 210, 170),
    ];

    private static NetLink[] StarLinks(string hub) =>
    [
        new(hub, "Север"),
        new(hub, "Восток"),
        new(hub, "Юг"),
        new(hub, "Запад"),
    ];

    public static IReadOnlyList<NetworkLevel> All { get; } =
    [
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Прямой канал",
            Objective = "Передайте каждый пакет из узла связи прямо в район назначения.",
            Description = "После аварии уцелел только **узел связи**: от него идут каналы во все районы. Пакет стоит в узле, а схема отвечает, в какой соседний узел передать его дальше. Блок «Пакет» знает **цель** — район, куда пакет должен попасть.",
            Hint = "Соедините выход «цель» блока «Пакет» с «Выходом».",
            Nodes = Star("Узел связи"),
            Links = StarLinks("Узел связи"),
            Packets = [new(1, "Узел связи", "Север"), new(2, "Узел связи", "Восток"), new(3, "Узел связи", "Юг"), new(4, "Узел связи", "Запад")],
            AllowedNodes = [PacketNode.Id],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000002"),
            Order = 2,
            Title = "Через центр",
            Objective = "Районы связаны только с центром. Доставьте пакеты между районами через него.",
            Description = "Прямых каналов между районами нет — все они идут через **Центр**. Если пакет в районе, его нужно отправить в Центр. Если он уже в Центре — прямо в район назначения. Где пакет сейчас, показывает выход «здесь».",
            Hint = "«Если»: a — «здесь», знак «=», b — Центр. В ветке «да» отдайте «цель», в ветке «нет» — константу «Центр».",
            Nodes = Star("Центр"),
            Links = StarLinks("Центр"),
            Packets = [new(1, "Север", "Юг"), new(2, "Запад", "Восток"), new(3, "Центр", "Север"), new(4, "Восток", "Запад")],
            AllowedNodes = [PacketNode.Id, "const", "if-compare", "if", "compare"],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000003"),
            Order = 3,
            Title = "Ретрансляторы",
            Objective = "Передавайте пакеты по цепочке ретрансляторов 1–8 в сторону цели.",
            Description = "Вдоль реки стоят ретрансляторы с номерами от 1 до 8, каждый связан только с соседними номерами. Если цель больше текущего номера — пакет идёт к номеру на 1 больше, иначе — на 1 меньше.",
            Hint = "«Если»: a — «цель», знак «>», b — «здесь». В ветке «да» отдайте «здесь» + 1, в ветке «нет» — «здесь» − 1.",
            Nodes = [.. Enumerable.Range(1, 8).Select(number => new NetNode(number.ToString(), 90 + (number - 1) * 117, number % 2 == 0 ? 205 : 135))],
            Links = [.. Enumerable.Range(1, 7).Select(number => new NetLink(number.ToString(), (number + 1).ToString()))],
            Packets = [new(1, "2", "6"), new(2, "7", "3"), new(3, "1", "8"), new(4, "5", "4")],
            AllowedNodes = [PacketNode.Id, "const", "if", "compare", "math"],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000004"),
            Order = 4,
            Title = "Обрыв кольца",
            Objective = "Кольцо районов разорвано. Доставьте пакеты в обход обрыва.",
            Description = "Районы соединены по кольцу, но канал **Порт — Завод** оборван. В списке «соседи» только рабочие каналы. Правило простое: не возвращай пакет туда, откуда он пришёл. Если идти больше некуда (тупик у обрыва) — придётся вернуться.",
            Hint = "«Если» первый сосед ≠ «откуда» — отдайте его. Иначе отдайте последнего соседа: его номер в списке равен длине списка.",
            Nodes = [new("Парк", 500, 40), new("Порт", 790, 108), new("Завод", 790, 236), new("Рынок", 500, 300), new("Вокзал", 210, 236), new("Школа", 210, 108)],
            Links = [new("Парк", "Порт"), new("Порт", "Завод", Broken: true), new("Завод", "Рынок"), new("Рынок", "Вокзал"), new("Вокзал", "Школа"), new("Школа", "Парк")],
            Packets = [new(1, "Парк", "Завод"), new(2, "Завод", "Школа"), new(3, "Порт", "Рынок"), new(4, "Вокзал", "Порт")],
            AllowedNodes = [PacketNode.Id, "const", "if", "compare", "list-item", "length", "category:logic"],
            MaxHops = 12,
        },
    ];
}
