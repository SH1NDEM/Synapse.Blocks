using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Games.Robots;

/// <summary>Блок «Робот»: показания датчиков в начале такта.</summary>
public sealed class RobotNode : NodeDefinition
{
    public const string Id = "robot";

    public override string TypeId => Id;
    public override string Label => "Робот";
    public override string Category => NodeCategories.Game;
    public override string Description => "Датчики робота: сколько клеток свободно впереди, свободно ли слева и справа, что на полу под роботом, лежит ли здесь батарейка и какой идёт такт.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataOut("front", "впереди", DataType.Number),
        Port.DataOut("left", "слева", DataType.Bool),
        Port.DataOut("right", "справа", DataType.Bool),
        Port.DataOut("floor", "пол", DataType.Text),
        Port.DataOut("battery", "батарейка", DataType.Bool),
        Port.DataOut("step", "такт", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.ProgramInput.Items;
        if (items.Length < 6)
            throw context.Error("Блок «Робот» работает только в игре «Робот-курьер».");
        return outputPort switch
        {
            "front" => items[0],
            "left" => items[1],
            "right" => items[2],
            "floor" => items[3],
            "battery" => items[4],
            _ => items[5]
        };
    }
}

public static class RobotNodes
{
    /// <summary>Встроенные блоки и блок «Робот».</summary>
    public static NodeRegistry Registry { get; } = Create();

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.Default.CloneDefinitions();
        registry.Register(new RobotNode());
        return registry;
    }
}

/// <summary>
/// Задания робота-курьера — пять разных идей: остановиться вовремя, объехать стену,
/// читать метки на полу, собирать батарейки и пройти лабиринт по правилу правой руки.
/// </summary>
public static class RobotLevels
{
    private static readonly string[] Basic = ["const", "if-compare", "if"];

    public static IReadOnlyList<RobotLevel> All { get; } =
    [
        new RobotLevel
        {
            Id = Guid.Parse("70b07000-0000-4000-8000-000000000001"),
            Order = 1,
            Title = "Остановись на финише",
            Objective = "Робот едет по коридору. Остановите его ровно на клетке финиша.",
            Description = "Робот не видит всё поле сразу — он знает только то, что говорят его **датчики**. Каждый такт схема отвечает одной командой: «вперёд» или «стоп».",
            Hint = "«Если»: a — «пол» робота, знак «=», b — «финиш». Ветка «да» → «Выход» с «Значением» «стоп», ветка «нет» → «Выход» с «вперёд».",
            Map = ["#########", "#S.....F#", "#########"],
            MaxSteps = 20,
            AllowedNodes = [RobotNode.Id, .. Basic],
        },
        new RobotLevel
        {
            Id = Guid.Parse("70b07000-0000-4000-8000-000000000002"),
            Order = 2,
            Title = "Стена впереди",
            Objective = "Коридор поворачивает. Когда впереди стена, поворачивайте направо. На финише — стоп.",
            Description = "Датчик **«впереди»** показывает, сколько свободных клеток перед роботом. Если там **0** — следующий шаг будет в стену. Команда «направо» поворачивает робота и делает шаг.",
            Hint = "Сначала проверьте финиш, потом «впереди» = 0 → «направо», иначе «вперёд». Порядок проверок важен: на финише впереди тоже стена.",
            Map = ["#########", "#S.....##", "######.##", "#F.....##", "#########"],
            MaxSteps = 30,
            AllowedNodes = [RobotNode.Id, .. Basic],
        },
        new RobotLevel
        {
            Id = Guid.Parse("70b07000-0000-4000-8000-000000000003"),
            Order = 3,
            Title = "Метки на полу",
            Objective = "На площади нет стен — путь показывают метки: красная — направо, синяя — налево.",
            Description = "Так ездят роботы на складах: по полу нарисованы метки, а робот читает их **датчиком цвета**. Датчик «пол» говорит: «пусто», «красный», «синий» или «финиш».",
            Hint = "Три проверки «пола» подряд: «финиш» → «стоп», «красный» → «направо», «синий» → «налево». Во всех остальных случаях — «вперёд».",
            Map = ["###########", "#S..R.....#", "#.........#", "#...B..F..#", "#.........#", "###########"],
            MaxSteps = 30,
            AllowedNodes = [RobotNode.Id, .. Basic],
        },
        new RobotLevel
        {
            Id = Guid.Parse("70b07000-0000-4000-8000-000000000004"),
            Order = 4,
            Title = "Батарейки",
            Objective = "Соберите все батарейки по дороге и остановитесь на финише.",
            Description = "Курьер не только едет, но и **работает руками**. Команда «взять» подбирает батарейку с клетки, на которой стоит робот, — на это уходит один такт. Остановиться можно, только когда собраны все.",
            Hint = "Первой поставьте проверку «батарейка» → «взять». Дальше — как во втором задании: финиш, стена впереди, вперёд.",
            Map = ["#########", "#S.*...##", "######*##", "#F..*..##", "#########"],
            MaxSteps = 40,
            AllowedNodes = [RobotNode.Id, .. Basic],
        },
        new RobotLevel
        {
            Id = Guid.Parse("70b07000-0000-4000-8000-000000000005"),
            Order = 5,
            Title = "Лабиринт",
            Objective = "Проведите робота через лабиринт к финишу по правилу правой руки.",
            Description = "Есть старый способ выйти из лабиринта: **держаться рукой за правую стену**. Если справа проход — поворачивай направо. Если нет — иди вперёд. Если и впереди стена — налево. А в тупике — назад.",
            Hint = "Порядок проверок: финиш → «стоп»; «справа» свободно → «направо»; «впереди» больше 0 → «вперёд»; «слева» свободно → «налево»; иначе — «назад». Датчики «справа» и «слева» отвечают «да/нет» — их удобно подать прямо в блок «Условие».",
            Map =
            [
                "###########",
                "#S..#.....#",
                "##.##.###.#",
                "#..#..#...#",
                "#.##.##.###",
                "#....#..F.#",
                "###########",
            ],
            MaxSteps = 60,
            AllowedNodes = [RobotNode.Id, .. Basic],
        },
    ];
}
