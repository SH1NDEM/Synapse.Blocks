using Synapse.Blocks.Models;

namespace Synapse.Blocks.Services;

/// <summary>Сколько обучающих уровней пройдено и открыты ли игры города.</summary>
public sealed record TrainingStatus(int Done, int Total, LevelDefinition? Next)
{
    /// <summary>Игры открыты, когда пройдены все обучающие уровни (или в кампании их нет).</summary>
    public bool Unlocked => Done >= Total;

    public string NextUrl => Next is null ? "game" : $"game?level={Next.Id}";

    public static TrainingStatus Open { get; } = new(0, 0, null);

    public static TrainingStatus From(IEnumerable<LevelDefinition> levels, IReadOnlySet<Guid> completed)
    {
        var training = levels.Where(level => level.Training).OrderBy(level => level.Order).ToList();
        return new(training.Count(level => completed.Contains(level.Id)), training.Count,
            training.FirstOrDefault(level => !completed.Contains(level.Id)));
    }
}

/// <summary>Проверяет, пройдено ли обучение. Кампания кэшируется в <see cref="LevelStore"/>, прогресс — в браузере.</summary>
public sealed class TrainingGate(LevelStore levels, ProgressStore progress)
{
    public async Task<TrainingStatus> LoadAsync()
    {
        try
        {
            var campaign = await levels.LoadGameAsync();
            var completed = await progress.LoadAsync();
            return TrainingStatus.From(campaign, completed);
        }
        catch
        {
            // Без связи с сервером не запираем игры: они работают без кампании.
            return TrainingStatus.Open;
        }
    }
}
