namespace Synapse.Blocks.Models;

public enum BlockKind
{
    Input,
    Operation,
    Condition,
    Loop,
    Output
}

public enum OutputPort
{
    // Next — обычная связь; остальные значения описывают ветки условия и цикла.
    Next,
    True,
    False,
    Repeat,
    Done
}

public enum TargetPort
{
    // Return используется только нижним входом цикла.
    Main,
    Return
}

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
    RunPanel
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
    public List<BlockKind> AllowedBlocks { get; set; } = Enum.GetValues<BlockKind>().ToList();
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

public sealed class BlockNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public BlockKind Kind { get; set; }
    public string Config { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class BlockConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public OutputPort Port { get; set; } = OutputPort.Next;
    public TargetPort TargetPort { get; set; } = TargetPort.Main;
}

public sealed class BlockProgram
{
    // Граф намеренно состоит из простых списков, чтобы его было легко сериализовать.
    public List<BlockNode> Nodes { get; set; } = [];
    public List<BlockConnection> Connections { get; set; } = [];
}

public sealed record ProgramExecution(
    // VisitedNodes нужен интерфейсу для подсветки реального пути выполнения.
    bool Success,
    string Output,
    string Error,
    IReadOnlyList<Guid> VisitedNodes,
    IReadOnlyList<ExecutionStep> Steps);

/// <summary>Снимок значения до и после выполнения одного блока.</summary>
public sealed record ExecutionStep(
    Guid NodeId,
    BlockKind Kind,
    string Input,
    string Output,
    string Detail);

public sealed record TestRunResult(
    Guid TestId,
    string Name,
    bool Passed,
    string Input,
    string Expected,
    string Actual,
    string Error,
    IReadOnlyList<Guid> VisitedNodes);

public static class BlockCatalog
{
    public static string Label(BlockKind kind) => kind switch
    {
        BlockKind.Input => "Вход",
        BlockKind.Operation => "Операция",
        BlockKind.Condition => "Условие",
        BlockKind.Loop => "Цикл",
        BlockKind.Output => "Выход",
        _ => kind.ToString()
    };

    public static string Kicker(BlockKind kind) => kind switch
    {
        BlockKind.Input => "ДАННЫЕ",
        BlockKind.Operation => "КОМАНДА",
        BlockKind.Condition => "ВЕТВЛЕНИЕ",
        BlockKind.Loop => "ПОВТОР",
        BlockKind.Output => "РЕЗУЛЬТАТ",
        _ => "БЛОК"
    };

    public static string Description(BlockKind kind) => kind switch
    {
        BlockKind.Input => "Получает данные текущего теста",
        BlockKind.Operation => "Изменяет значение по команде",
        BlockKind.Condition => "Выбирает синюю или красную ветку",
        BlockKind.Loop => "Повторяет цепочку заданное число раз",
        BlockKind.Output => "Возвращает итог для проверки",
        _ => ""
    };

    public static string Placeholder(BlockKind kind) => kind switch
    {
        BlockKind.Operation => "+ 2, * 3, set готово, append !",
        BlockKind.Condition => "> 20, == ключ, contains ошибка",
        BlockKind.Loop => "input или число повторов",
        _ => ""
    };
}
