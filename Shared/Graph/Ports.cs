namespace Synapse.Blocks.Graph;

/// <summary>Тип данных порта. Any принимает и отдаёт что угодно.</summary>
public enum DataType
{
    Any,
    Number,
    Text,
    Bool,
    List
}

/// <summary>Провод исполнения задаёт порядок действий, провод данных передаёт значение.</summary>
public enum PortKind
{
    Exec,
    Data
}

public enum PortDirection
{
    In,
    Out
}

/// <summary>Описание порта блока. Порты задаются в определении блока, а не в экземпляре.</summary>
public sealed record PortDef(string Id, string Label, PortKind Kind, PortDirection Direction, DataType Type = DataType.Any)
{
    /// <summary>
    /// Значение по умолчанию для неподключённого входа данных. Если задано, значение можно
    /// ввести прямо в блоке (оно хранится в <see cref="GraphNode.Params"/> под Id порта).
    /// Null — вход обязательно нужно подключить проводом.
    /// </summary>
    public string? InlineDefault { get; init; }

    /// <summary>Выход исполнения открывает вложенную ветку (тело цикла): когда ветка кончается, управление возвращается в блок.</summary>
    public bool OpensScope { get; init; }

    /// <summary>Выход исполнения можно оставить неподключённым без предупреждения.</summary>
    public bool Optional { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Подпись для сообщений: Label или, если он пуст, Id.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Id : Label;

    public bool IsExec => Kind == PortKind.Exec;
    public bool IsData => Kind == PortKind.Data;
    public bool IsInput => Direction == PortDirection.In;
    public bool IsOutput => Direction == PortDirection.Out;
    public bool HasInline => Kind == PortKind.Data && Direction == PortDirection.In && InlineDefault is not null;
}

/// <summary>Короткие фабрики, чтобы определения блоков читались как таблица портов.</summary>
public static class Port
{
    public static PortDef ExecIn(string id = "in", string label = "")
        => new(id, label, PortKind.Exec, PortDirection.In);

    public static PortDef ExecOut(string id = "next", string label = "")
        => new(id, label, PortKind.Exec, PortDirection.Out);

    public static PortDef Scope(string id, string label)
        => new(id, label, PortKind.Exec, PortDirection.Out) { OpensScope = true };

    /// <summary>Вход данных, который обязательно подключается проводом.</summary>
    public static PortDef DataIn(string id, string label, DataType type = DataType.Any)
        => new(id, label, PortKind.Data, PortDirection.In, type);

    /// <summary>Вход данных со значением, которое можно ввести прямо в блоке.</summary>
    public static PortDef Inline(string id, string label, DataType type, string defaultValue)
        => new(id, label, PortKind.Data, PortDirection.In, type) { InlineDefault = defaultValue };

    public static PortDef DataOut(string id, string label, DataType type = DataType.Any)
        => new(id, label, PortKind.Data, PortDirection.Out, type);
}

public static class DataTypes
{
    public static string Label(DataType type) => type switch
    {
        DataType.Number => "число",
        DataType.Text => "текст",
        DataType.Bool => "логическое",
        DataType.List => "список",
        _ => "любое"
    };

    /// <summary>
    /// Можно ли соединить выход типа <paramref name="from"/> со входом типа <paramref name="to"/>.
    /// Любое значение превращается в текст; текст может оказаться числом и проверяется при выполнении.
    /// </summary>
    public static bool CanConnect(DataType from, DataType to)
    {
        if (from == to || from == DataType.Any || to == DataType.Any) return true;
        if (to == DataType.Text) return true;
        if (from == DataType.Text && to == DataType.Number) return true;
        return false;
    }

    /// <summary>Приводит значение к типу входа или объясняет, почему это невозможно.</summary>
    public static bool TryCoerce(Value value, DataType type, out Value result, out string error)
    {
        error = "";
        result = value;
        switch (type)
        {
            case DataType.Any:
                return true;
            case DataType.Number:
                if (value.TryGetNumber(out var number))
                {
                    result = Value.FromNumber(number);
                    return true;
                }
                error = $"ожидалось число, а пришло «{value}» ({value.KindLabel})";
                return false;
            case DataType.Text:
                result = value.IsText ? value : Value.FromText(value.ToString());
                return true;
            case DataType.Bool:
                if (value.TryGetBool(out var boolean))
                {
                    result = Value.FromBool(boolean);
                    return true;
                }
                error = $"ожидалось «да/нет» (логическое), а пришло «{value}» ({value.KindLabel})";
                return false;
            case DataType.List:
                if (value.IsList) return true;
                error = $"ожидался список, а пришло «{value}» ({value.KindLabel})";
                return false;
            default:
                return true;
        }
    }
}
