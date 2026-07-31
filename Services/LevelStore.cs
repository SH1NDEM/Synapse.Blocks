using System.Text.Json;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class LevelStore(HttpClient http)
{
    public Task<List<LevelDefinition>> LoadGameAsync()
    {
        return LoadFromJsonAsync();
    }

    public Task<List<LevelDefinition>> LoadEditorAsync()
    {
        return LoadFromJsonAsync();
    }

    private async Task<List<LevelDefinition>> LoadFromJsonAsync()
    {
        var json = await http.GetStringAsync("levels.json");

        var levels = JsonSerializer.Deserialize(
            json,
            AppJsonSerializerContext.Default.ListLevelDefinition);

        if (levels is null)
            throw new InvalidOperationException("Не удалось прочитать levels.json.");

        return Normalize(levels);
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

    private static List<LevelDefinition> Normalize(List<LevelDefinition> levels)
    {
        // Нормализация также страхует уровни, импортированные из более старой версии редактора.
        var ordered = levels.OrderBy(level => level.Order).ThenBy(level => level.Title).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var level = ordered[index];
            level.Order = index + 1;
            level.Chapter = Math.Max(1, level.Chapter);
            if (string.IsNullOrWhiteSpace(level.Location))
                level.Location = "Город Ноль";
            if (string.IsNullOrWhiteSpace(level.StoryIntro))
                level.StoryIntro = level.Description;
            if (string.IsNullOrWhiteSpace(level.StorySuccess))
                level.StorySuccess =
                    "Операция завершена. В городе восстановлен ещё один участок сети.";
            level.AllowedBlocks = level.AllowedBlocks.Distinct().ToList();
            if (!level.AllowedBlocks.Contains(BlockKind.Input)) level.AllowedBlocks.Insert(0, BlockKind.Input);
            if (!level.AllowedBlocks.Contains(BlockKind.Output)) level.AllowedBlocks.Add(BlockKind.Output);
            level.Tests ??= [];
            level.IntroSteps ??= [];
            foreach (var step in level.IntroSteps)
                step.MediaUrl ??= "";
        }
        return ordered;
    }
}
