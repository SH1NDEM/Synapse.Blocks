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

/// <summary>Задания связи: город после грозы — от одной АТС до кольца с обрывом.</summary>
public static class NetworkLevels
{
    private static NetDecor River(string path, string label = "", double x = 0, double y = 0) => new("river", path, label, x, y);

    public static IReadOnlyList<NetworkLevel> All { get; } =
    [
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Одна АТС",
            Objective = "Передайте каждый пакет с АТС прямо в район назначения.",
            Description = "Гроза оборвала связь в городе. Уцелела только **АТС**: от неё идут кабели во все районы. Схема отвечает, в какой соседний узел передать пакет. Блок «Пакет» знает **цель**.",
            Hint = "Соедините выход «цель» блока «Пакет» с «Выходом».",
            Nodes = [new("АТС", 500, 170, NetNodeKind.Exchange), new("Вокзал", 500, 50), new("Порт", 790, 120), new("Завод", 760, 280), new("Рынок", 240, 280), new("Школа", 210, 120)],
            Links = [new("АТС", "Вокзал"), new("АТС", "Порт"), new("АТС", "Завод"), new("АТС", "Рынок"), new("АТС", "Школа")],
            Packets = [new(1, "АТС", "Вокзал"), new(2, "АТС", "Порт"), new(3, "АТС", "Рынок")],
            AllowedNodes = [PacketNode.Id],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000002"),
            Order = 2,
            Title = "Шлюз по умолчанию",
            Objective = "Если цель — сосед, отдайте пакет ей. Иначе — первому соседу в списке: это шлюз.",
            Description = "Город разделён рекой. На каждом берегу своя АТС, между ними — кабель по мосту. Если цель среди соседей — отдай пакет ей, иначе — первому соседу (шлюзу).",
            Hint = "Блок «Есть в списке»: список — «соседи», значение — «цель». Подайте его в «Условие»: в ветке «да» отдайте «цель», в ветке «нет» — первый элемент «соседей».",
            Nodes =
            [
                new("АТС-Север", 380, 110, NetNodeKind.Exchange), new("АТС-Юг", 620, 230, NetNodeKind.Exchange),
                new("Вокзал", 150, 70), new("Школа", 160, 200),
                new("Порт", 850, 140), new("Рынок", 820, 300),
            ],
            Links =
            [
                new("АТС-Север", "АТС-Юг"),
                new("АТС-Север", "Вокзал"), new("АТС-Север", "Школа"),
                new("АТС-Юг", "Порт"), new("АТС-Юг", "Рынок"),
                new("Школа", "Рынок", Broken: true),
            ],
            Packets = [new(1, "Школа", "Рынок"), new(2, "Порт", "Вокзал"), new(3, "АТС-Юг", "Школа"), new(4, "Вокзал", "Порт")],
            AllowedNodes = [PacketNode.Id, "const", "if-compare", "if", "compare", "list-item", "list-has"],
            Decor = [River("M 0 300 C 260 280, 400 170, 500 170 S 760 90, 1000 60", "река Синь", 260, 314)],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000003"),
            Order = 3,
            Title = "Ретрансляторы",
            Objective = "Передавайте пакеты по цепочке ретрансляторов 1–7 в сторону цели.",
            Description = "Вдоль реки стоят ретрансляторы с номерами от 1 до 7, каждый связан только с соседними номерами. Если цель больше текущего номера — пакет идёт к номеру на 1 больше, иначе — на 1 меньше.",
            Hint = "«Если»: a — «цель», знак «>», b — «здесь». В ветке «да» отдайте «здесь» + 1, в ветке «нет» — «здесь» − 1.",
            Nodes = [.. Enumerable.Range(1, 7).Select(number => new NetNode(number.ToString(), 110 + (number - 1) * 130, number % 2 == 0 ? 200 : 130, NetNodeKind.Tower))],
            Links = [.. Enumerable.Range(1, 6).Select(number => new NetLink(number.ToString(), (number + 1).ToString()))],
            Packets = [new(1, "2", "6"), new(2, "7", "3"), new(3, "4", "1")],
            AllowedNodes = [PacketNode.Id, "const", "if-compare", "if", "compare", "math"],
            Decor = [River("M 0 290 C 300 270, 700 310, 1000 285", "река Синь", 120, 318)],
        },
        new NetworkLevel
        {
            Id = Guid.Parse("5a7e0002-0000-4000-8000-000000000004"),
            Order = 4,
            Title = "Обрыв кольца",
            Objective = "Районы стоят на кольце, кабель Завод — Стадион оборван. Доставьте пакеты в обход обрыва.",
            Description = "Районы соединены кабелем по кругу, но гроза оборвала его между **Заводом и Стадионом**. Правило: не возвращай пакет туда, откуда он пришёл. Если идти больше некуда (тупик у обрыва) — придётся вернуться.",
            Hint = "«Если» первый сосед ≠ «откуда» — отдайте его. Иначе отдайте последнего соседа: его номер в списке равен длине списка.",
            Nodes = [new("Вокзал", 500, 50), new("Порт", 760, 110), new("Завод", 760, 230), new("Стадион", 500, 290), new("Рынок", 240, 230), new("Школа", 240, 110)],
            Links =
            [
                new("Вокзал", "Порт"), new("Порт", "Завод"), new("Завод", "Стадион", Broken: true),
                new("Стадион", "Рынок"), new("Рынок", "Школа"), new("Школа", "Вокзал"),
            ],
            Packets = [new(1, "Вокзал", "Стадион"), new(2, "Рынок", "Порт"), new(3, "Завод", "Школа")],
            AllowedNodes = [PacketNode.Id, "const", "if-compare", "if", "compare", "list-item", "length", "list-has", "category:logic"],
            MaxHops = 12,
        },
    ];
}
