using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Games.Station;

/// <summary>Блок «Поезд»: раскладывает данные прибывающего поезда по выходам.</summary>
public sealed class TrainNode : NodeDefinition
{
    public const string Id = "train";

    public override string TypeId => Id;
    public override string Label => "Поезд";
    public override string Category => NodeCategories.Game;
    public override string Description => "Данные поезда, который подходит к станции: номер, откуда идёт, сколько вагонов, тип, а также занятые и свободные пути.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataOut("number", "номер", DataType.Number),
        Port.DataOut("from", "откуда", DataType.Text),
        Port.DataOut("cars", "вагонов", DataType.Number),
        Port.DataOut("kind", "тип", DataType.Text),
        Port.DataOut("busy", "занятые", DataType.List),
        Port.DataOut("free", "свободные", DataType.List),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.ProgramInput.Items;
        if (items.Length < 6)
            throw context.Error("Блок «Поезд» работает только в игре «Станция».");
        return outputPort switch
        {
            "number" => items[0],
            "from" => items[1],
            "cars" => items[2],
            "kind" => items[3],
            "busy" => items[4],
            _ => items[5]
        };
    }
}

public static class StationNodes
{
    /// <summary>Встроенные блоки и блок «Поезд».</summary>
    public static NodeRegistry Registry { get; } = Create();

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.Default.CloneDefinitions();
        registry.Register(new TrainNode());
        return registry;
    }
}

/// <summary>Задания станции — от «всё на один путь» до свободных путей и экспрессов.</summary>
public static class StationLevels
{
    private static StationTrain W(int number, double arrive, int cars = 4, string kind = StationTrain.Passenger, double dwell = 6)
        => new(number, StationSide.West, cars, kind, arrive, dwell);

    private static StationTrain E(int number, double arrive, int cars = 4, string kind = StationTrain.Passenger, double dwell = 6)
        => new(number, StationSide.East, cars, kind, arrive, dwell);

    private static readonly string[] Basic = [TrainNode.Id, "const"];

    /// <summary>Вокзал «Синапс-Порт»: короткие и длинные перроны, грузовой двор.</summary>
    private static readonly StationTrack[] TerminusTracks =
    [
        new(1, 6), new(2, 6), new(3, 10), new(4, 10),
        new(5, 12, StationTrackKind.Freight), new(6, 12, StationTrackKind.Freight),
    ];

    public static IReadOnlyList<StationLevel> All { get; } =
    [
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Первый путь",
            Objective = "Принимайте каждый поезд на путь 1.",
            Description = "Станция Синапса открывается после ремонта. Пока работает только **путь 1**. На каждый поезд схема должна ответить номером пути — блок «Выход» отправляет ответ диспетчеру.",
            Hint = "Подключите к «Выходу» константу 1.",
            Tracks = [new(1, 8), new(2, 8)],
            Trains = [W(101, 0), E(102, 20), W(103, 40)],
            AllowedNodes = Basic,
            Rule = (_, track) => track == 1 ? null : "сегодня работает только путь 1.",
        },
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000002"),
            Order = 2,
            Title = "Две стороны",
            Objective = "Поезда с запада — на путь 1, с востока — на путь 2.",
            Description = "Поезда идут с двух сторон. Блок «Поезд» подсказывает, **откуда** идёт состав: «запад» или «восток». Поставьте «Если» и отправьте поезда каждой стороны на свой путь.",
            Hint = "Поставьте «Если»: a — «откуда», знак «=», b — запад. В ветке «да» — «Выход» с 1, в ветке «нет» — «Выход» с 2.",
            Tracks = [new(1, 8), new(2, 8)],
            Trains = [W(201, 0), E(202, 8), W(203, 24), E(204, 30), E(205, 50), W(206, 52)],
            AllowedNodes = [.. Basic, "if-compare", "if", "compare", "text-check"],
            Rule = (train, track) => (train.From, track) switch
            {
                (StationSide.West, 1) or (StationSide.East, 2) => null,
                (StationSide.West, _) => "поезда с запада принимаются на путь 1.",
                _ => "поезда с востока принимаются на путь 2."
            },
        },
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000003"),
            Order = 3,
            Title = "Длинные составы",
            Objective = "Составы длиннее 6 вагонов — на путь 3, остальные — на путь 1.",
            Description = "Пути 1 и 2 короткие: до **6 вагонов**. Длинный путь 3 вмещает 12 вагонов, и его держат для длинных составов. Сколько вагонов в поезде, показывает выход «вагонов».",
            Hint = "«Если»: a — «вагонов», знак «>», b — 6. В ветке «да» ответ 3, в ветке «нет» — 1.",
            Tracks = [new(1, 6), new(2, 6), new(3, 12)],
            Trains = [W(301, 0, cars: 4), E(302, 10, cars: 10, kind: StationTrain.Freight), W(303, 20, cars: 6), W(304, 30, cars: 12, kind: StationTrain.Freight), E(305, 40, cars: 3), E(306, 50, cars: 7)],
            AllowedNodes = [.. Basic, "if", "compare"],
            Rule = (train, track) => train.Cars > 6
                ? track == 3 ? null : "длинные составы принимаются на путь 3."
                : track == 1 ? null : "короткие составы принимаются на путь 1, путь 3 держим для длинных.",
        },
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000004"),
            Order = 4,
            Title = "Свободный путь",
            Objective = "Принимайте каждый поезд на любой свободный путь.",
            Description = "Час пик: поезда приходят один за другим, и пути не успевают освободиться. Выход «свободные» — список путей, на которых сейчас никого нет. Возьмите из него любой номер.",
            Hint = "«Элемент списка» с номером 1 даст первый свободный путь.",
            Tracks = [new(1, 8), new(2, 8), new(3, 8)],
            Trains = [W(401, 0), E(402, 4), W(403, 8), E(404, 20), W(405, 22), E(406, 26), W(407, 40), E(408, 42)],
            AllowedNodes = [.. Basic, "list-item", "length", "if", "compare"],
        },
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000005"),
            Order = 5,
            Title = "Экспресс",
            Objective = "Экспрессы — только на путь 1. Остальные поезда — на любой свободный путь, кроме 1.",
            Description = "Через станцию идут экспрессы. Они стоят недолго и всегда занимают **путь 1**, поэтому остальным составам этот путь занимать нельзя. Тип поезда — на выходе «тип».",
            Hint = "«Если» тип = экспресс — ответ 1. Иначе возьмите последний элемент списка «свободные»: его номер равен длине списка.",
            Tracks = [new(1, 8), new(2, 8), new(3, 8), new(4, 8)],
            Trains =
            [
                W(501, 0), E(502, 3, kind: StationTrain.Express, dwell: 2), W(503, 6, cars: 7, kind: StationTrain.Freight), E(504, 10),
                W(505, 22, kind: StationTrain.Express, dwell: 2), W(506, 25), E(507, 28, cars: 8, kind: StationTrain.Freight), E(508, 45, kind: StationTrain.Express, dwell: 2),
            ],
            AllowedNodes = [.. Basic, "category:logic", "list-item", "length", "math", "text-check"],
            Rule = (train, track) => train.Kind == StationTrain.Express
                ? track == 1 ? null : "экспрессы принимаются только на путь 1."
                : track == 1 ? "путь 1 держим для экспрессов." : null,
        },

        // ─────────── Тупиковый вокзал «Синапс-Порт» ───────────
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000006"),
            Order = 6,
            Title = "Тупиковый вокзал",
            StationName = "Синапс-Порт",
            Layout = StationLayout.Terminus,
            Objective = "Пассажирские поезда — к перронам (пути 1–4), грузовые — на грузовой двор (пути 5–6).",
            Description = "У реки стоит **тупиковый вокзал**: все поезда приходят с востока, упираются в концевые упоры и уезжают обратно. Пути 1–4 идут вдоль островных перронов, а пути 5–6 — на **грузовой двор** к складам: перрона там нет. Свободные пути в списке идут по порядку — сначала пассажирские, потом грузовые.",
            Hint = "«Если» тип = грузовой — отдайте последний элемент списка «свободные» (его номер равен длине списка). Иначе — первый.",
            Tracks = TerminusTracks,
            Trains =
            [
                E(601, 0, cars: 4, kind: StationTrain.Suburban, dwell: 8), E(602, 3, cars: 10, kind: StationTrain.Freight, dwell: 10),
                E(603, 6, cars: 6, kind: StationTrain.LongDistance, dwell: 8), E(604, 9, cars: 12, kind: StationTrain.Freight, dwell: 10),
                E(605, 12, cars: 3, kind: StationTrain.Suburban, dwell: 8), E(606, 20, cars: 5, kind: StationTrain.Express, dwell: 4),
                E(607, 24, cars: 9, kind: StationTrain.Freight, dwell: 10), E(608, 30, cars: 4, kind: StationTrain.Suburban, dwell: 8),
            ],
            AllowedNodes = [.. Basic, "category:logic", "list-item", "length", "list-has"],
            Rule = (train, track) => train.IsFreight && track < 5 ? "грузовые составы принимаются на грузовой двор (пути 5–6)." : null,
        },
        new StationLevel
        {
            Id = Guid.Parse("5a7e0001-0000-4000-8000-000000000007"),
            Order = 7,
            Title = "Короткие и длинные перроны",
            StationName = "Синапс-Порт",
            Layout = StationLayout.Terminus,
            Objective = "Грузовые — на пути 5–6. Поезда до 6 вагонов — к коротким перронам 1–2, длиннее — к длинным 3–4.",
            Description = "Перроны вокзала разной длины: у путей 1–2 помещается **6 вагонов**, у путей 3–4 — **10**. Длинные перроны держат для дальних поездов. Внутри каждой пары берите путь, который свободен: проверить это поможет блок «Есть в списке».",
            Hint = "Выбор «из пары» повторяется трижды — соберите его в свой блок: входы «первый», «второй», «свободные»; внутри «Если» по «Есть в списке» и два «Вернуть».",
            Tracks = TerminusTracks,
            Trains =
            [
                E(701, 0, cars: 4, kind: StationTrain.Suburban, dwell: 8), E(702, 2, cars: 9, kind: StationTrain.LongDistance, dwell: 8),
                E(703, 4, cars: 11, kind: StationTrain.Freight, dwell: 10), E(704, 6, cars: 5, kind: StationTrain.Suburban, dwell: 8),
                E(705, 9, cars: 10, kind: StationTrain.LongDistance, dwell: 8), E(706, 12, cars: 8, kind: StationTrain.Freight, dwell: 10),
                E(707, 19, cars: 3, kind: StationTrain.Suburban, dwell: 8), E(708, 21, cars: 8, kind: StationTrain.LongDistance, dwell: 8),
                E(709, 25, cars: 12, kind: StationTrain.Freight, dwell: 10), E(710, 26, cars: 6, kind: StationTrain.Express, dwell: 4),
            ],
            AllowedNodes = [.. Basic, "category:logic", "list-item", "length", "list-has"],
            Rule = (train, track) => train.IsFreight
                ? track >= 5 ? null : "грузовые составы принимаются на грузовой двор (пути 5–6)."
                : train.Cars <= 6 && track is 3 or 4 ? "короткие поезда — к коротким перронам 1–2, длинные держим для дальних." : null,
        },
    ];
}
