using System.Text.Json;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public static class LevelDrafts
{
    public static List<LevelDefinition> Snapshot(IEnumerable<LevelDefinition> levels)
    {
        var json = JsonSerializer.Serialize(levels.ToList(), AppJsonSerializerContext.Default.ListLevelDefinition);
        return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListLevelDefinition)!;
    }
    public static LevelDefinition Create(int order, int chapter = 1) => new()
    {
        Order = order,
        Chapter = chapter,
        Title = $"Новый уровень {order}",
        Location = "Новый сектор",
        Tests = [new() { Name = "Основная проверка", Input = "0", ExpectedOutput = "0" }]
    };

    public static LevelDefinition Duplicate(LevelDefinition source, int order)
    {
        // Copy all task constraints and future model fields without changing the original.
        var copy = Snapshot([source])[0];
        copy.Id = Guid.NewGuid();
        copy.Order = order;
        copy.Title = $"{source.Title} — копия";
        foreach (var test in copy.Tests) test.Id = Guid.NewGuid();
        foreach (var step in copy.IntroSteps) step.Id = Guid.NewGuid();
        return copy;
    }
}
