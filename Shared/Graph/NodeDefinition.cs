namespace Synapse.Blocks.Graph;

public enum ParamType
{
    /// <summary>Произвольный текст.</summary>
    Text,
    /// <summary>Число.</summary>
    Number,
    /// <summary>Один вариант из списка <see cref="ParamDef.Choices"/>.</summary>
    Choice,
    /// <summary>Имя переменной программы.</summary>
    Variable,
    /// <summary>Любое значение: число, текст, true/false или JSON-список.</summary>
    Value
}

public sealed record ParamChoice(string Id, string Label);

/// <summary>Настройка блока, которая не является портом: вид операции, имя переменной и т. п.</summary>
public sealed record ParamDef(string Id, string Label, ParamType Type, string Default = "")
{
    public IReadOnlyList<ParamChoice> Choices { get; init; } = [];

    public static ParamDef Choice(string id, string label, params ParamChoice[] choices)
        => new(id, label, ParamType.Choice, choices.Length > 0 ? choices[0].Id : "") { Choices = choices };

    public static ParamDef Variable(string id = "variable", string label = "Переменная")
        => new(id, label, ParamType.Variable);

    public string ChoiceLabel(string id) => Choices.FirstOrDefault(choice => choice.Id == id)?.Label ?? id;
}

/// <summary>Категории для палитры блоков и для правил уровня (<c>category:math</c>).</summary>
public static class NodeCategories
{
    public const string Flow = "flow";
    public const string Data = "data";
    public const string Math = "math";
    public const string Logic = "logic";
    public const string Text = "text";
    public const string Memory = "memory";
    public const string List = "list";

    public static string Label(string category) => category switch
    {
        Flow => "Управление",
        Data => "Данные",
        Math => "Математика",
        Logic => "Логика",
        Text => "Текст",
        Memory => "Память",
        List => "Списки",
        _ => category
    };
}

public enum ExecAction
{
    /// <summary>Перейти по выходу исполнения.</summary>
    Continue,
    /// <summary>Войти во вложенную ветку; когда она закончится, блок выполнится снова.</summary>
    EnterScope,
    /// <summary>Завершить программу с результатом.</summary>
    Finish
}

/// <summary>Что блок исполнения просит сделать дальше.</summary>
public readonly record struct ExecResult(ExecAction Action, string Port, Value Result, string Detail)
{
    public static ExecResult Next(string port, string detail = "") => new(ExecAction.Continue, port, Value.Null, detail);
    public static ExecResult Enter(string port, string detail = "") => new(ExecAction.EnterScope, port, Value.Null, detail);
    public static ExecResult Finish(Value result, string detail = "") => new(ExecAction.Finish, "", result, detail);
}

/// <summary>Ошибка выполнения, понятная игроку. Бросается из блоков через <see cref="NodeContext.Error"/>.</summary>
public sealed class GraphRuntimeException(string message, Guid? nodeId = null) : Exception(message)
{
    public Guid? NodeId { get; internal set; } = nodeId;
}

/// <summary>
/// Определение вида блока: порты, настройки и поведение. Один экземпляр на тип,
/// все блоки этого типа в программах ссылаются на него по <see cref="TypeId"/>.
///
/// Блок исполнения (есть порты Exec) реализует <see cref="Execute"/>.
/// «Чистый» блок (только порты данных) реализует <see cref="Evaluate"/> — он вычисляется
/// по требованию, когда его значение понадобилось другому блоку.
/// </summary>
public abstract class NodeDefinition
{
    private bool? _isPure;

    public abstract string TypeId { get; }
    public abstract string Label { get; }
    public virtual string Category => NodeCategories.Data;
    public virtual string Description => "";
    public abstract IReadOnlyList<PortDef> Ports { get; }
    public virtual IReadOnlyList<ParamDef> Params => [];

    /// <summary>С этого блока начинается выполнение. В программе должен быть ровно один такой блок.</summary>
    public virtual bool IsEntry => false;

    /// <summary>Сколько блоков этого типа должно быть в программе (null — без ограничения).</summary>
    public virtual int? MinPerGraph => null;
    public virtual int? MaxPerGraph => null;

    public bool IsPure => _isPure ??= !Ports.Any(port => port.IsExec);

    public PortDef? FindPort(string id) => Ports.FirstOrDefault(port => port.Id == id);
    public ParamDef? FindParam(string id) => Params.FirstOrDefault(param => param.Id == id);
    public IEnumerable<PortDef> Inputs => Ports.Where(port => port.IsInput);
    public IEnumerable<PortDef> Outputs => Ports.Where(port => port.IsOutput);

    public virtual ExecResult Execute(NodeContext context)
        => throw new GraphRuntimeException($"Блок «{Label}» не умеет выполняться как команда.");

    public virtual Value Evaluate(NodeContext context, string outputPort)
        => throw new GraphRuntimeException($"Блок «{Label}» не вычисляет значения.");

    /// <summary>Дополнительные проверки, специфичные для блока. Общие проверки делает <see cref="GraphValidator"/>.</summary>
    public virtual IEnumerable<string> Validate(GraphNode node, GraphProgram program) => [];
}
