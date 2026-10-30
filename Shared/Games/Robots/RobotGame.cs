using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Games.Robots;

/// <summary>
/// Задание игры «Робот-курьер». Поле — клетки: <c>#</c> стена, <c>.</c> пол, <c>S</c> старт,
/// <c>F</c> финиш, <c>R</c> красная метка, <c>B</c> синяя метка, <c>*</c> батарейка.
/// Каждый такт схема получает показания датчиков и отвечает одной командой.
/// </summary>
public sealed class RobotLevel
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Objective { get; init; } = "";
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public IReadOnlyList<string> Map { get; init; } = [];
    /// <summary>Куда смотрит робот на старте: 0 — вверх, 1 — вправо, 2 — вниз, 3 — влево.</summary>
    public int StartDirection { get; init; } = 1;
    public int MaxSteps { get; init; } = 60;
    public IReadOnlyList<string> AllowedNodes { get; init; } = [];

    public int Width => Map.Count == 0 ? 0 : Map.Max(row => row.Length);
    public int Height => Map.Count;

    public char Cell(int x, int y) => y < 0 || y >= Map.Count || x < 0 || x >= Map[y].Length ? '#' : Map[y][x];

    public (int X, int Y) Start
    {
        get
        {
            for (var y = 0; y < Map.Count; y++)
            {
                var x = Map[y].IndexOf('S');
                if (x >= 0) return (x, y);
            }
            return (1, 1);
        }
    }

    public IEnumerable<(int X, int Y)> Batteries()
    {
        for (var y = 0; y < Map.Count; y++)
            for (var x = 0; x < Map[y].Length; x++)
                if (Map[y][x] == '*') yield return (x, y);
    }

    public LevelDefinition AsLevel() => new()
    {
        Id = Id,
        Order = Order,
        Title = Title,
        Objective = Objective,
        Description = Description,
        Hint = Hint,
        Direction = "Роботы",
        AllowedNodes = [.. LevelNodes.Mandatory, .. AllowedNodes],
    };
}

/// <summary>Показания датчиков робота в начале такта.</summary>
public sealed record RobotSensors(int Front, bool Left, bool Right, string Floor, bool Battery, int Step);

/// <summary>Один такт: где был робот, что видели датчики и какую команду дала схема.</summary>
public sealed record RobotStep(int Index, int X, int Y, int Direction, RobotSensors Sensors, string Command, string Error)
{
    public double Start => Index * RobotSimulator.StepTime;
    public double End => Start + RobotSimulator.StepTime;
}

public sealed record RobotRun(bool Success, string Error, IReadOnlyList<RobotStep> Steps, double Duration);

/// <summary>Прогон робота по полю. На первой ошибке (удар о стену, неверная остановка) прогон останавливается.</summary>
public static class RobotSimulator
{
    /// <summary>Секунды на один такт анимации.</summary>
    public const double StepTime = .55;

    private static readonly int[] Dx = [0, 1, 0, -1];
    private static readonly int[] Dy = [-1, 0, 1, 0];

    public static Value Input(RobotSensors sensors) => Value.FromList(
    [
        Value.FromNumber(sensors.Front),
        Value.FromBool(sensors.Left),
        Value.FromBool(sensors.Right),
        Value.FromText(sensors.Floor),
        Value.FromBool(sensors.Battery),
        Value.FromNumber(sensors.Step),
    ]);

    public static string FloorName(char cell) => cell switch
    {
        'F' => "финиш",
        'R' => "красный",
        'B' => "синий",
        _ => "пусто"
    };

    /// <summary>Нормализует команду: «вперед», «прямо» → «вперёд» и т. п. Пустая строка — команда не понята.</summary>
    public static string ParseCommand(Value value) => value.ToString().Trim().ToLowerInvariant().Replace('ё', 'е') switch
    {
        "вперед" or "прямо" => "вперёд",
        "налево" or "влево" => "налево",
        "направо" or "вправо" => "направо",
        "назад" or "развернуться" or "разворот" => "назад",
        "взять" or "подобрать" => "взять",
        "стоп" or "стой" => "стоп",
        _ => ""
    };

    public static RobotSensors Sense(RobotLevel level, int x, int y, int direction, ISet<(int X, int Y)> batteries, int step)
    {
        bool Free(int cx, int cy) => level.Cell(cx, cy) != '#';
        var front = 0;
        var (fx, fy) = (x, y);
        while (Free(fx + Dx[direction], fy + Dy[direction]) && front < 50)
        {
            front++;
            fx += Dx[direction];
            fy += Dy[direction];
        }
        var left = (direction + 3) % 4;
        var right = (direction + 1) % 4;
        return new RobotSensors(front,
            Free(x + Dx[left], y + Dy[left]),
            Free(x + Dx[right], y + Dy[right]),
            FloorName(level.Cell(x, y)),
            batteries.Contains((x, y)),
            step);
    }

    public static RobotRun Run(RobotLevel level, GraphProgram program, GraphRunner runner)
    {
        var prepared = runner.Prepare(program);
        var steps = new List<RobotStep>();
        var (x, y) = level.Start;
        var direction = level.StartDirection;
        var batteries = level.Batteries().ToHashSet();

        RobotRun Fail(RobotSensors sensors, string command, string error)
        {
            steps.Add(new RobotStep(steps.Count, x, y, direction, sensors, command, error));
            return new RobotRun(false, $"Такт {steps.Count}: {error}", steps, steps.Count * StepTime);
        }

        for (var step = 1; step <= level.MaxSteps; step++)
        {
            var sensors = Sense(level, x, y, direction, batteries, step);
            var execution = runner.Run(prepared, Input(sensors), recordSteps: false);
            if (!execution.Success)
                return Fail(sensors, "", $"схема остановилась с ошибкой: {execution.Error}");
            var command = ParseCommand(execution.Output);
            if (command.Length == 0)
                return Fail(sensors, execution.Output.ToString(), $"робот не понял команду «{execution.Output}». Команды: вперёд, налево, направо, назад, взять, стоп.");

            if (command == "стоп")
            {
                if (level.Cell(x, y) != 'F')
                    return Fail(sensors, command, "робот остановился не на финише.");
                if (batteries.Count > 0)
                    return Fail(sensors, command, $"на поле остались батарейки: {batteries.Count}.");
                steps.Add(new RobotStep(steps.Count, x, y, direction, sensors, command, ""));
                return new RobotRun(true, "", steps, steps.Count * StepTime);
            }
            if (command == "взять")
            {
                if (!batteries.Remove((x, y)))
                    return Fail(sensors, command, "здесь нечего брать.");
                steps.Add(new RobotStep(steps.Count, x, y, direction, sensors, command, ""));
                continue;
            }

            var turned = command switch
            {
                "налево" => (direction + 3) % 4,
                "направо" => (direction + 1) % 4,
                "назад" => (direction + 2) % 4,
                _ => direction
            };
            // Шаг записывается с направлением до поворота: анимация сама повернёт робота и покажет удар.
            if (level.Cell(x + Dx[turned], y + Dy[turned]) == '#')
                return Fail(sensors, command, "робот врезался в стену!");
            steps.Add(new RobotStep(steps.Count, x, y, direction, sensors, command, ""));
            direction = turned;
            x += Dx[direction];
            y += Dy[direction];
        }
        var last = Sense(level, x, y, direction, batteries, level.MaxSteps + 1);
        return Fail(last, "", $"робот так и не остановился за {level.MaxSteps} тактов.");
    }

    /// <summary>Куда ведёт команда: направление после поворота (для анимации).</summary>
    public static int Turned(int direction, string command) => command switch
    {
        "налево" => (direction + 3) % 4,
        "направо" => (direction + 1) % 4,
        "назад" => (direction + 2) % 4,
        _ => direction
    };

    public static (int X, int Y) Ahead(int x, int y, int direction) => (x + Dx[direction], y + Dy[direction]);
}
