using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Games.Station;

/// <summary>С какой стороны поезд подходит к станции. Уезжает он в противоположную сторону.</summary>
public enum StationSide
{
    West,
    East
}

/// <summary>Путь станции с платформой. Length — сколько вагонов на нём помещается.</summary>
public sealed record StationTrack(int Number, int Length);

/// <summary>Поезд из расписания: прибывает в момент Arrive (секунды), стоит Dwell секунд.</summary>
public sealed record StationTrain(int Number, StationSide From, int Cars, string Kind, double Arrive, double Dwell = 6)
{
    public const string Passenger = "пассажирский";
    public const string Express = "экспресс";
    public const string Freight = "грузовой";

    public string FromText => From == StationSide.West ? "запад" : "восток";
}

/// <summary>
/// Задание игры «Станция». Схема игрока — диспетчер: на каждый прибывающий поезд она
/// отвечает номером пути. Rule — особое требование задания (null — подходит любой свободный путь).
/// </summary>
public sealed class StationLevel
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Objective { get; init; } = "";
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public IReadOnlyList<StationTrack> Tracks { get; init; } = [];
    public IReadOnlyList<StationTrain> Trains { get; init; } = [];
    public IReadOnlyList<string> AllowedNodes { get; init; } = [];
    public Func<StationTrain, int, string?>? Rule { get; init; }

    /// <summary>То же задание в виде уровня — чтобы палитра и проверки блоков работали как в кампании.</summary>
    public LevelDefinition AsLevel() => new()
    {
        Id = Id,
        Order = Order,
        Title = Title,
        Objective = Objective,
        Description = Description,
        Hint = Hint,
        Direction = "Станция",
        AllowedNodes = [.. LevelNodes.Mandatory, .. AllowedNodes],
    };
}

/// <summary>Как обошлись с одним поездом: какой путь выбран и когда поезд въехал, встал и уехал.</summary>
public sealed record StationTrainResult(StationTrain Train, int? Track, string Answer, string Error, double Start, double Stop, double Leave)
{
    public bool Passed => Error.Length == 0;
    public double End => Leave + StationSimulator.TravelOut;
}

public sealed record StationRun(bool Success, string Error, IReadOnlyList<StationTrainResult> Trains, double Duration);

/// <summary>
/// Прогон расписания: поезда приходят по очереди, для каждого запускается схема игрока.
/// Ответ проверяется: такой путь есть, он свободен, поезд на нём помещается и выполнено
/// требование задания. На первой ошибке прогон останавливается.
/// </summary>
public static class StationSimulator
{
    /// <summary>Секунды на въезд от границы станции до платформы и на выезд обратно.</summary>
    public const double TravelIn = 5;
    public const double TravelOut = 5;

    /// <summary>
    /// Входные данные схемы для поезда — список:
    /// номер, откуда, вагонов, тип, занятые пути, свободные пути. Их раскладывает блок «Поезд».
    /// </summary>
    public static Value Input(StationTrain train, IReadOnlyList<int> busy, IReadOnlyList<int> free) => Value.FromList(
    [
        Value.FromNumber(train.Number),
        Value.FromText(train.FromText),
        Value.FromNumber(train.Cars),
        Value.FromText(train.Kind),
        Value.FromList(busy.Select(track => Value.FromNumber(track))),
        Value.FromList(free.Select(track => Value.FromNumber(track))),
    ]);

    public static StationRun Run(StationLevel level, GraphProgram program, GraphRunner runner)
    {
        var results = new List<StationTrainResult>();
        var occupied = new List<(int Track, double Until, int Train)>();

        foreach (var train in level.Trains.OrderBy(train => train.Arrive))
        {
            var time = train.Arrive;
            var holders = occupied.Where(item => item.Until > time).ToList();
            var busy = holders.Select(item => item.Track).Distinct().Order().ToList();
            var free = level.Tracks.Select(track => track.Number).Where(number => !busy.Contains(number)).Order().ToList();

            var execution = runner.Run(program, Input(train, busy, free));
            var answer = execution.Success ? execution.Output.ToString() : "";
            var stop = time + TravelIn;
            var leave = stop + train.Dwell;
            string error;
            int? track = null;

            if (!execution.Success)
                error = $"схема остановилась с ошибкой: {execution.Error}";
            else if (!execution.Output.TryGetNumber(out var number) || number % 1 != 0)
                error = $"схема ответила «{answer}», а нужен номер пути.";
            else if (level.Tracks.FirstOrDefault(item => item.Number == (int)number) is not { } chosen)
                error = $"пути {answer} на станции нет. Есть пути: {string.Join(", ", level.Tracks.Select(item => item.Number))}.";
            else if (holders.FirstOrDefault(item => item.Track == chosen.Number) is { Train: > 0 } holder)
                error = $"путь {chosen.Number} занят поездом №{holder.Train}.";
            else if (train.Cars > chosen.Length)
                error = $"поезд из {train.Cars} вагонов не помещается на путь {chosen.Number} (до {chosen.Length} вагонов).";
            else if (level.Rule?.Invoke(train, chosen.Number) is { } ruleError)
                error = ruleError;
            else
            {
                error = "";
                track = chosen.Number;
                occupied.Add((chosen.Number, leave + TravelOut, train.Number));
            }

            results.Add(new StationTrainResult(train, track, answer, error, time, stop, leave));
            if (error.Length > 0)
                return new StationRun(false, $"Поезд №{train.Number}: {error}", results, time + 3);
        }

        return new StationRun(true, "", results, results.Count == 0 ? 0 : results.Max(result => result.End));
    }
}
