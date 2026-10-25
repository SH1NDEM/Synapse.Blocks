using Synapse.Blocks.Graph;
using Synapse.Blocks.Graph.Nodes;

namespace Synapse.Blocks.Models;

/// <summary>Вид поля, которое можно показать внутри вступительного пояснения.</summary>
public enum IntroFieldKind
{
    None,
    Input,
    Output,
    Operation,
    Condition,
    Loop
}

/// <summary>Область игрового интерфейса, подсвечиваемая во время шага вступления.</summary>
public enum IntroHighlightTarget
{
    None,
    Mission,
    Objective,
    InputData,
    OutputData,
    Tests,
    Workspace,
    BlockLibrary,
    RunPanel,
    Description
}

public sealed class LevelDefinition
{
    // Уровень не хранит правильную схему: только контракт задачи и набор проверок.
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public int Chapter { get; set; } = 1;
    public string Title { get; set; } = "Новый уровень";
    public string Direction { get; set; } = "Программирование";
    public string Location { get; set; } = "Город Ноль";
    public string Description { get; set; } = "";
    public string StoryIntro { get; set; } = "";
    public string StorySuccess { get; set; } = "";
    public string Objective { get; set; } = "";
    public string InputDescription { get; set; } = "";
    public string OutputDescription { get; set; } = "";
    public string Hint { get; set; } = "";

    /// <summary>
    /// Блоки, доступные игроку: TypeId (<c>"math"</c>) или целая категория (<c>"category:logic"</c>).
    /// «Вход» и «Выход» доступны всегда.
    /// </summary>
    public List<string> AllowedNodes { get; set; } = [.. LevelNodes.Mandatory];

    /// <summary>Учебные ограничения: проверяют не только ответ, но и способ решения.</summary>
    public List<GraphRule> Rules { get; set; } = [];

    public List<LevelTestCase> Tests { get; set; } = [];
    public List<LevelIntroStep> IntroSteps { get; set; } = [];
}

public sealed class LevelIntroStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Новое пояснение";
    public string Body { get; set; } = "Объясните игроку важную часть задания.";
    // Ссылка на GIF/картинку или data URL, полученный из загрузки в редакторе.
    public string MediaUrl { get; set; } = "";
    public IntroFieldKind FieldKind { get; set; }
    public string FieldValue { get; set; } = "";
    public IntroHighlightTarget HighlightTarget { get; set; }
}

public sealed class LevelTestCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Проверка";
    public string Input { get; set; } = "0";
    public string ExpectedOutput { get; set; } = "0";
    public bool Hidden { get; set; }
}

/// <summary>Какие блоки уровень даёт игроку.</summary>
public static class LevelNodes
{
    /// <summary>Без этих блоков программу не собрать, поэтому они доступны в любом уровне.</summary>
    public static readonly IReadOnlyList<string> Mandatory = [StartNode.Id, OutputNode.Id];

    public static bool IsMandatory(string typeId)
        => Mandatory.Contains(typeId, StringComparer.OrdinalIgnoreCase);

    public static bool Allows(LevelDefinition level, NodeDefinition definition)
        => IsMandatory(definition.TypeId) || level.AllowedNodes.Any(target => GraphRules.Matches(target, definition));

    public static IEnumerable<NodeDefinition> Available(LevelDefinition level, NodeRegistry registry)
        => registry.All.Where(definition => Allows(level, definition));

    /// <summary>Проверяет, что элемент списка — известный блок или непустая категория.</summary>
    public static bool IsKnownTarget(string target, NodeRegistry registry)
        => target.StartsWith(GraphRules.CategoryPrefix, StringComparison.OrdinalIgnoreCase)
            ? registry.All.Any(definition => GraphRules.Matches(target, definition))
            : registry.TryGet(target, out _);
}
