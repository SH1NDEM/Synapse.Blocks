using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Synapse.Blocks.Data;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Services;

public sealed class LevelStore(IJSRuntime js)
{
    // Версия ключа позволяет обновлять встроенную кампанию, не смешивая разные схемы данных.
    private const string StorageKey = "synapse-csharp-levels-v2";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<LevelDefinition>> LoadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            var levels = SeedLevels.Create();
            await SaveAsync(levels);
            return levels;
        }

        try
        {
            var levels = JsonSerializer.Deserialize<List<LevelDefinition>>(json, _jsonOptions);
            return levels is { Count: > 0 } ? Normalize(levels) : SeedLevels.Create();
        }
        catch (JsonException)
        {
            return SeedLevels.Create();
        }
    }

    public async Task SaveAsync(IEnumerable<LevelDefinition> levels)
    {
        var normalized = Normalize(levels.ToList());
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public string Export(IEnumerable<LevelDefinition> levels)
        => JsonSerializer.Serialize(Normalize(levels.ToList()), _jsonOptions);

    public List<LevelDefinition> Import(string json)
    {
        var levels = JsonSerializer.Deserialize<List<LevelDefinition>>(json, _jsonOptions)
            ?? throw new InvalidOperationException("В файле нет списка уровней.");
        if (levels.Count == 0)
            throw new InvalidOperationException("Нужен хотя бы один уровень.");
        return Normalize(levels);
    }

    public async Task ResetAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

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
        }
        return ordered;
    }
}
