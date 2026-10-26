using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Games.Security;

/// <summary>
/// Запрос к серверу города. Attack — правильное решение для этого запроса: true — его нужно
/// заблокировать. Note объясняет игроку, что это за запрос, если схема ошиблась.
/// </summary>
public sealed record SecRequest(int Number, string From, int Port, string Action, bool Attack, string Note, string Password = "", int Attempts = 0);

/// <summary>
/// Задание игры «Защита серверов». Схема игрока — межсетевой экран: на каждый запрос она отвечает
/// «да» (пропустить к серверу) или «нет» (заблокировать).
/// </summary>
public sealed class SecurityLevel
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Objective { get; init; } = "";
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public IReadOnlyList<SecRequest> Requests { get; init; } = [];
    public IReadOnlyList<string> AllowedNodes { get; init; } = [];
    /// <summary>Чёрный список адресов, если он есть в задании (показывается игроку).</summary>
    public IReadOnlyList<string> Blacklist { get; init; } = [];

    public LevelDefinition AsLevel() => new()
    {
        Id = Id,
        Order = Order,
        Title = Title,
        Objective = Objective,
        Description = Description,
        Hint = Hint,
        Direction = "Защита",
        AllowedNodes = [.. LevelNodes.Mandatory, .. AllowedNodes],
    };
}

/// <summary>Решение по одному запросу. Allowed — что ответила схема (null — схема не ответила).</summary>
public sealed record SecurityRequestResult(SecRequest Request, bool? Allowed, string Answer, string Error, double Start)
{
    public bool Passed => Error.Length == 0;
    /// <summary>Запрос дошёл до экрана и решение принято.</summary>
    public double Decided => Start + SecuritySimulator.ToWall;
    public double End => Decided + SecuritySimulator.AfterWall;
}

public sealed record SecurityRun(bool Success, string Error, IReadOnlyList<SecurityRequestResult> Requests, double Duration);

/// <summary>
/// Прогон запросов по очереди. Для каждого запускается схема игрока, её ответ сравнивается
/// с правильным: пропущенная атака и заблокированный обычный пользователь — обе ошибки.
/// На первой ошибке прогон останавливается.
/// </summary>
public static class SecuritySimulator
{
    /// <summary>Секунды от отправителя до межсетевого экрана и после него (к серверу или обратно).</summary>
    public const double ToWall = 1;
    public const double AfterWall = .9;
    public const double Gap = .25;

    /// <summary>Входные данные схемы — список: номер, откуда, порт, действие, пароль, попыток.</summary>
    public static Value Input(SecRequest request) => Value.FromList(
    [
        Value.FromNumber(request.Number),
        Value.FromText(request.From),
        Value.FromNumber(request.Port),
        Value.FromText(request.Action),
        Value.FromText(request.Password),
        Value.FromNumber(request.Attempts),
    ]);

    /// <summary>Понимает ответ схемы: да/нет, true/false, «пропустить»/«заблокировать», 1/0.</summary>
    public static bool? ParseDecision(Value value)
    {
        if (value.TryGetBool(out var flag)) return flag;
        if (value.IsNumber && value.TryGetNumber(out var number)) return number switch { 1 => true, 0 => false, _ => null };
        return value.ToString().Trim().ToLowerInvariant() switch
        {
            "да" or "пропустить" or "разрешить" => true,
            "нет" or "заблокировать" or "блок" or "запретить" => false,
            _ => null
        };
    }

    public static SecurityRun Run(SecurityLevel level, GraphProgram program, GraphRunner runner)
    {
        var results = new List<SecurityRequestResult>();
        var time = 0.0;
        foreach (var request in level.Requests)
        {
            var execution = runner.Run(program, Input(request));
            var answer = execution.Success ? execution.Output.ToString() : "";
            bool? allowed = execution.Success ? ParseDecision(execution.Output) : null;
            string error;
            if (!execution.Success)
                error = $"схема остановилась с ошибкой: {execution.Error}";
            else if (allowed is null)
                error = $"схема ответила «{answer}», а нужно «да» (пропустить) или «нет» (заблокировать).";
            else if (allowed == true && request.Attack)
                error = $"пропущена атака — {request.Note}";
            else if (allowed == false && !request.Attack)
                error = $"заблокирован обычный пользователь — {request.Note}";
            else
                error = "";

            var result = new SecurityRequestResult(request, allowed, answer, error, time);
            results.Add(result);
            if (!result.Passed)
                return new SecurityRun(false, $"Запрос №{request.Number} от {request.From}: {error}", results, result.End);
            time = result.End + Gap;
        }
        return new SecurityRun(true, "", results, results.Count == 0 ? 0 : results[^1].End);
    }
}
