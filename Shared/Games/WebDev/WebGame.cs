using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Games.WebDev;

/// <summary>
/// Анкета, которую житель отправил с сайта. Valid — правильное решение: true — анкету нужно принять.
/// Note объясняет игроку, что с анкетой, если схема ошиблась.
/// </summary>
public sealed record WebForm(int Number, string Name, int Age, string Email, bool Valid, string Note);

/// <summary>
/// Задание игры «Веб-разработчик». Схема игрока — проверка формы на сайте: на каждую анкету
/// она отвечает «да» (принять) или «нет» (показать ошибку).
/// </summary>
public sealed class WebLevel
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Objective { get; init; } = "";
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public string Site { get; init; } = "";
    public IReadOnlyList<WebForm> Forms { get; init; } = [];
    public IReadOnlyList<string> AllowedNodes { get; init; } = [];

    public LevelDefinition AsLevel() => new()
    {
        Id = Id,
        Order = Order,
        Title = Title,
        Objective = Objective,
        Description = Description,
        Hint = Hint,
        Direction = "Веб",
        AllowedNodes = [.. LevelNodes.Mandatory, .. AllowedNodes],
    };
}

/// <summary>Решение по одной анкете. Accepted — что ответила схема (null — схема не ответила).</summary>
public sealed record WebFormResult(WebForm Form, bool? Accepted, string Answer, string Error, double Start)
{
    public bool Passed => Error.Length == 0;
    /// <summary>Анкета заполнена и нажата кнопка — схема приняла решение.</summary>
    public double Decided => Start + WebSimulator.Typing;
    public double End => Decided + WebSimulator.Result;
}

public sealed record WebRun(bool Success, string Error, IReadOnlyList<WebFormResult> Forms, double Duration);

/// <summary>Анкеты проверяются по очереди; на первой ошибке прогон останавливается.</summary>
public static class WebSimulator
{
    /// <summary>Секунды на заполнение полей и на показ результата.</summary>
    public const double Typing = 1.6;
    public const double Result = 1.1;
    public const double Gap = .3;

    /// <summary>Входные данные схемы — список: имя, возраст, почта.</summary>
    public static Value Input(WebForm form) => Value.FromList(
    [
        Value.FromText(form.Name),
        Value.FromNumber(form.Age),
        Value.FromText(form.Email),
    ]);

    /// <summary>Понимает ответ схемы: да/нет, true/false, «принять»/«ошибка», 1/0.</summary>
    public static bool? ParseDecision(Value value)
    {
        if (value.TryGetBool(out var flag)) return flag;
        if (value.IsNumber && value.TryGetNumber(out var number)) return number switch { 1 => true, 0 => false, _ => null };
        return value.ToString().Trim().ToLowerInvariant() switch
        {
            "да" or "принять" or "ок" or "ok" => true,
            "нет" or "ошибка" or "отклонить" => false,
            _ => null
        };
    }

    public static WebRun Run(WebLevel level, GraphProgram program, GraphRunner runner)
    {
        var prepared = runner.Prepare(program);
        var results = new List<WebFormResult>();
        var time = 0.0;
        foreach (var form in level.Forms)
        {
            var execution = runner.Run(prepared, Input(form), recordSteps: false);
            var answer = execution.Success ? execution.Output.ToString() : "";
            bool? accepted = execution.Success ? ParseDecision(execution.Output) : null;
            string error;
            if (!execution.Success)
                error = $"схема остановилась с ошибкой: {execution.Error}";
            else if (accepted is null)
                error = $"схема ответила «{answer}», а нужно «да» (принять) или «нет» (показать ошибку).";
            else if (accepted == true && !form.Valid)
                error = $"сайт принял неправильную анкету — {form.Note}";
            else if (accepted == false && form.Valid)
                error = $"{form.Name} не смог(ла) зарегистрироваться, хотя всё заполнено верно — {form.Note}";
            else
                error = "";

            var result = new WebFormResult(form, accepted, answer, error, time);
            results.Add(result);
            if (!result.Passed)
                return new WebRun(false, $"Анкета №{form.Number} ({form.Name}): {error}", results, result.End);
            time = result.End + Gap;
        }
        return new WebRun(true, "", results, results.Count == 0 ? 0 : results[^1].End);
    }
}
