using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Data;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class LevelStore(IJSRuntime js)
{
    // v5 остаётся текущим ключом. Старые ключи используются только как резервные
    // копии и больше никогда не удаляются автоматически.
    private const string StorageKey = "synapse-csharp-levels-v5";
    private const string IntroRecoveryMarkerKey = "synapse-csharp-intros-recovered-v2";
    private static readonly string[] PreviousStorageKeys =
    [
        "synapse-csharp-levels-v4",
        "synapse-csharp-levels-v3",
        "synapse-csharp-levels-v2",
        "synapse-csharp-levels-v1"
    ];
    public async Task<List<LevelDefinition>> LoadAsync()
    {
        try
        {
            return await LoadCoreAsync();
        }
        catch (Exception)
        {
            // Повреждённое или переполненное хранилище не должно блокировать запуск игры.
            return SeedLevels.Create();
        }
    }

    private async Task<List<LevelDefinition>> LoadCoreAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        string? migratedFromKey = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            foreach (var previousKey in PreviousStorageKeys)
            {
                json = await js.InvokeAsync<string?>("localStorage.getItem", previousKey);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    migratedFromKey = previousKey;
                    break;
                }
            }
            if (migratedFromKey is null)
            {
                var levels = SeedLevels.Create();
                await SaveAsync(levels);
                return levels;
            }
        }

        try
        {
            var levels = JsonSerializer.Deserialize(json!, AppJsonSerializerContext.Default.ListLevelDefinition);
            if (levels is not { Count: > 0 })
                return SeedLevels.Create();

            var needsCompactRewrite = levels.Any(level =>
                level.Title == "Усилитель сигнала" &&
                level.IntroSteps is { Count: > 1 } &&
                level.IntroSteps[1].MediaUrl?.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) == true);
            var normalized = Normalize(levels);
            var shouldSave = needsCompactRewrite;
            if (migratedFromKey is not null)
            {
                // В v4 уровни уже переставлены и могут содержать новые уровни автора —
                // сохраняем их все. Ограничение до четырёх относилось только к старой
                // встроенной кампании v1/v2/v3.
                if (migratedFromKey is "synapse-csharp-levels-v3" or "synapse-csharp-levels-v2" or "synapse-csharp-levels-v1")
                {
                    normalized = normalized.Take(4).ToList();
                    if (normalized.Count >= 3)
                        (normalized[1], normalized[2]) = (normalized[2], normalized[1]);
                }
                for (var index = 0; index < normalized.Count; index++)
                    normalized[index].Order = index + 1;
                shouldSave = true;
            }

            var recoveryPending = await IsIntroRecoveryPendingAsync();
            if (recoveryPending)
            {
                // В самом первом редакторе пользовательские окна сохранялись в v1.
                // Возвращаем их только для первых двух уровней и только один раз.
                shouldSave |= await RestoreFirstLevelsIntroStepsAsync(normalized);
            }

            if (shouldSave)
                await SaveAsync(normalized);
            if (recoveryPending)
                await js.InvokeVoidAsync("localStorage.setItem", IntroRecoveryMarkerKey, "1");

            return normalized;
        }
        catch (JsonException)
        {
            return SeedLevels.Create();
        }
    }

    public async Task SaveAsync(IEnumerable<LevelDefinition> levels)
    {
        var normalized = Normalize(levels.ToList());
        var json = JsonSerializer.Serialize(normalized, AppJsonSerializerContext.Default.ListLevelDefinition);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public string Export(IEnumerable<LevelDefinition> levels)
        => JsonSerializer.Serialize(Normalize(levels.ToList()), AppJsonSerializerContext.Default.ListLevelDefinition);

    public List<LevelDefinition> Import(string json)
    {
        var levels = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListLevelDefinition)
            ?? throw new InvalidOperationException("В файле нет списка уровней.");
        if (levels.Count == 0)
            throw new InvalidOperationException("Нужен хотя бы один уровень.");
        return Normalize(levels);
    }

    public async Task ResetAsync()
    {
        // Сброс меняет только активную кампанию. Старые данные остаются резервной
        // копией и не подмешиваются после осознанного сброса.
        await SaveAsync(SeedLevels.Create());
        await js.InvokeVoidAsync("localStorage.setItem", IntroRecoveryMarkerKey, "1");
    }

    private async Task<bool> IsIntroRecoveryPendingAsync()
    {
        try
        {
            var marker = await js.InvokeAsync<string?>("localStorage.getItem", IntroRecoveryMarkerKey);
            return marker != "1";
        }
        catch (JSException)
        {
            return false;
        }
    }

    private async Task<bool> RestoreFirstLevelsIntroStepsAsync(List<LevelDefinition> levels)
    {
        var candidates = new Dictionary<string, (int Score, List<LevelIntroStep> Steps)>(StringComparer.Ordinal);
        var seedLevels = SeedLevels.Create().ToDictionary(level => level.Title, StringComparer.Ordinal);

        // Ключи идут от нового к старому: берём самую свежую найденную версию
        // пользовательских окон, но ничего в резервных данных не изменяем.
        foreach (var key in PreviousStorageKeys)
        {
            try
            {
                var json = await js.InvokeAsync<string?>("localStorage.getItem", key);
                if (string.IsNullOrWhiteSpace(json)) continue;

                var storedLevels = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListLevelDefinition);
                if (storedLevels is null) continue;

                foreach (var storedLevel in storedLevels)
                {
                    if (storedLevel.IntroSteps is not { Count: > 0 })
                        continue;

                    var steps = storedLevel.IntroSteps.Select(CloneIntroStep).ToList();
                    var isBuiltInCopy = seedLevels.TryGetValue(storedLevel.Title, out var seedLevel) &&
                                        IntroStepsHaveSameContent(steps, seedLevel.IntroSteps);
                    var score = steps.Count + (isBuiltInCopy ? 0 : 1000);
                    if (!candidates.TryGetValue(storedLevel.Title, out var current) || score > current.Score)
                        candidates[storedLevel.Title] = (score, steps);
                }
            }
            catch (Exception)
            {
                // Один повреждённый старый ключ не должен мешать прочитать другой.
            }
        }

        var changed = false;
        foreach (var level in levels.Where(level => level.Order <= 2))
        {
            if (!candidates.TryGetValue(level.Title, out var candidate) || candidate.Steps.Count == 0)
                continue;

            level.IntroSteps = candidate.Steps;
            changed = true;
        }

        // Если резервной копии первого уровня не осталось, возвращаем известный
        // исходный набор его трёх окон с GIF во втором шаге.
        var firstLevel = levels.FirstOrDefault(level => level.Title == "Усилитель сигнала");
        if (firstLevel is { IntroSteps.Count: 0 })
        {
            var seedFirstLevel = SeedLevels.Create().First(level => level.Title == "Усилитель сигнала");
            firstLevel.IntroSteps = seedFirstLevel.IntroSteps.Select(CloneIntroStep).ToList();
            changed = true;
        }

        return changed;
    }

    private static bool IntroStepsHaveSameContent(IReadOnlyList<LevelIntroStep> left, IReadOnlyList<LevelIntroStep> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Title != right[index].Title ||
                left[index].Body != right[index].Body ||
                left[index].MediaUrl != right[index].MediaUrl ||
                left[index].FieldKind != right[index].FieldKind ||
                left[index].FieldValue != right[index].FieldValue ||
                left[index].HighlightTarget != right[index].HighlightTarget)
                return false;
        }
        return true;
    }

    private static LevelIntroStep CloneIntroStep(LevelIntroStep step) => new()
    {
        Id = step.Id,
        Title = step.Title,
        Body = step.Body,
        MediaUrl = step.MediaUrl ?? "",
        FieldKind = step.FieldKind,
        FieldValue = step.FieldValue,
        HighlightTarget = step.HighlightTarget
    };

    private static List<LevelDefinition> Normalize(List<LevelDefinition> levels)
    {
        // Нормализация также страхует уровни, импортированные из более старой версии редактора.
        var ordered = levels.OrderBy(level => level.Order).ThenBy(level => level.Title).ToList();
        var examples = SeedLevels.Create();
        for (var index = 0; index < ordered.Count; index++)
        {
            var level = ordered[index];
            var example = examples.FirstOrDefault(item => item.Title == level.Title);
            level.Order = index + 1;
            level.Chapter = Math.Max(1, level.Chapter);
            if (string.IsNullOrWhiteSpace(level.Location)) level.Location = example?.Location ?? "Город Ноль";
            if (string.IsNullOrWhiteSpace(level.StoryIntro)) level.StoryIntro = example?.StoryIntro ?? level.Description;
            if (string.IsNullOrWhiteSpace(level.StorySuccess)) level.StorySuccess = example?.StorySuccess ?? "Операция завершена. В городе восстановлен ещё один участок сети.";
            level.AllowedBlocks = level.AllowedBlocks.Distinct().ToList();
            if (!level.AllowedBlocks.Contains(BlockKind.Input)) level.AllowedBlocks.Insert(0, BlockKind.Input);
            if (!level.AllowedBlocks.Contains(BlockKind.Output)) level.AllowedBlocks.Add(BlockKind.Output);
            level.Tests ??= [];
            level.IntroSteps ??= [];
            foreach (var step in level.IntroSteps)
                step.MediaUrl ??= "";

            // GIF был частью второй подсказки первого уровня. Возвращаем его только
            // если пользователь не выбрал для этого окна собственный файл.
            if (level.Title == "Усилитель сигнала" && level.IntroSteps.Count > 1 &&
                (string.IsNullOrWhiteSpace(level.IntroSteps[1].MediaUrl) ||
                 level.IntroSteps[1].MediaUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)))
            {
                // Старый редактор сохранял GIF как base64 прямо в JSON. Это занимало
                // несколько мегабайт и переполняло localStorage при каждом обновлении.
                level.IntroSteps[1].MediaUrl = "gifs/connect-nodes.gif";
            }

        }
        return ordered;
    }
}
