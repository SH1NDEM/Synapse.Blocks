using System.Collections.Immutable;

namespace Synapse.Blocks.Graph.Nodes;

/// <summary>Встроенные блоки. Порядок регистрации = порядок в палитре.</summary>
public static class BuiltInNodes
{
    public static void RegisterAll(NodeRegistry registry)
    {
        // Управление
        registry.Register(new StartNode());
        registry.Register(new OutputNode());
        registry.Register(new IfNode());
        registry.Register(new RepeatNode());
        registry.Register(new WhileNode());
        registry.Register(new ForEachNode());

        // Память
        registry.Register(new GetVariableNode());
        registry.Register(new SetVariableNode());
        registry.Register(new ChangeVariableNode());

        // Данные и вычисления
        registry.Register(new ConstantNode());
        registry.Register(new MathNode());
        registry.Register(new CompareNode());
        registry.Register(new LogicNode());
        registry.Register(new NotNode());
        registry.Register(new IsTypeNode());
        registry.Register(new TextCheckNode());
        registry.Register(new JoinNode());
        registry.Register(new LengthNode());
        registry.Register(new ListItemNode());
    }
}

// ───────────────────────────── Управление ─────────────────────────────

/// <summary>Начало программы: отсюда идёт провод исполнения и выходят данные теста.</summary>
public sealed class StartNode : NodeDefinition
{
    public const string Id = "start";
    public override string TypeId => Id;
    public override string Label => "Вход";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Начало программы. Отдаёт входные данные текущего теста.";
    public override bool IsEntry => true;
    public override int? MinPerGraph => 1;
    public override int? MaxPerGraph => 1;

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecOut("next", "далее"),
        Port.DataOut("value", "данные"),
    ];

    public override ExecResult Execute(NodeContext context)
    {
        context.SetOutput("value", context.ProgramInput);
        return ExecResult.Next("next", "Входные данные получены");
    }
}

/// <summary>Конец программы: значение на входе — ответ, который сравнивается с тестом.</summary>
public sealed class OutputNode : NodeDefinition
{
    public const string Id = "output";
    public override string TypeId => Id;
    public override string Label => "Выход";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Завершает программу и возвращает результат для проверки.";
    public override int? MinPerGraph => 1;

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.DataIn("value", "результат"),
    ];

    public override ExecResult Execute(NodeContext context)
        => ExecResult.Finish(context.Input("value"), "Результат программы");
}

/// <summary>Ветвление по логическому значению.</summary>
public sealed class IfNode : NodeDefinition
{
    public override string TypeId => "if";
    public override string Label => "Условие";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Выбирает ветку «да» или «нет» по логическому значению.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.DataIn("condition", "условие", DataType.Bool),
        Port.ExecOut("true", "да"),
        Port.ExecOut("false", "нет"),
    ];

    public override ExecResult Execute(NodeContext context)
        => context.Bool("condition")
            ? ExecResult.Next("true", "Условие выполнено — ветка «да»")
            : ExecResult.Next("false", "Условие не выполнено — ветка «нет»");
}

/// <summary>Повторяет тело заданное число раз. Конец ветки тела сам возвращает управление сюда.</summary>
public sealed class RepeatNode : NodeDefinition
{
    public const int MaxCount = 1000;

    public override string TypeId => "repeat";
    public override string Label => "Повторить";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Выполняет ветку «тело» заданное число раз, затем продолжает по «после цикла».";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.Inline("count", "раз", DataType.Number, "3"),
        Port.Scope("body", "тело"),
        Port.ExecOut("done", "после цикла"),
        Port.DataOut("index", "номер повтора", DataType.Number),
    ];

    private sealed class State
    {
        public int Total;
        public int Completed;
    }

    public override ExecResult Execute(NodeContext context)
    {
        var state = context.GetState<State>();
        if (state is null)
        {
            // Число повторов читается один раз — при входе в цикл.
            var count = context.Number("count");
            if (count < 0 || count > MaxCount || count % 1 != 0)
                throw context.Error($"Число повторов должно быть целым от 0 до {MaxCount}, а получено {Value.FormatNumber(count)}.");
            state = new State { Total = (int)count };
            context.SetState(state);
        }
        else
        {
            state.Completed++;
        }

        if (state.Completed < state.Total)
        {
            context.SetOutput("index", Value.FromNumber(state.Completed + 1));
            return ExecResult.Enter("body", $"Повтор {state.Completed + 1} из {state.Total}");
        }

        context.SetState(null);
        return ExecResult.Next("done", $"Цикл завершён: {state.Total} повторов");
    }
}

/// <summary>Повторяет тело, пока условие истинно. Условие проверяется перед каждым повтором.</summary>
public sealed class WhileNode : NodeDefinition
{
    public override string TypeId => "while";
    public override string Label => "Пока";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Повторяет ветку «тело», пока условие истинно.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.DataIn("condition", "условие", DataType.Bool),
        Port.Scope("body", "тело"),
        Port.ExecOut("done", "после цикла"),
    ];

    public override ExecResult Execute(NodeContext context)
        => context.Bool("condition")
            ? ExecResult.Enter("body", "Условие истинно — выполняем тело")
            : ExecResult.Next("done", "Условие ложно — цикл завершён");
}

/// <summary>Выполняет тело для каждого элемента списка.</summary>
public sealed class ForEachNode : NodeDefinition
{
    public override string TypeId => "for-each";
    public override string Label => "Для каждого";
    public override string Category => NodeCategories.Flow;
    public override string Description => "Выполняет ветку «тело» для каждого элемента списка.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.DataIn("list", "список", DataType.List),
        Port.Scope("body", "тело"),
        Port.ExecOut("done", "после цикла"),
        Port.DataOut("item", "элемент"),
        Port.DataOut("index", "номер", DataType.Number),
    ];

    private sealed class State
    {
        public ImmutableArray<Value> Items;
        public int Position;
    }

    public override ExecResult Execute(NodeContext context)
    {
        var state = context.GetState<State>();
        if (state is null)
        {
            state = new State { Items = context.Input("list").Items };
            context.SetState(state);
        }
        else
        {
            state.Position++;
        }

        if (state.Position < state.Items.Length)
        {
            var item = state.Items[state.Position];
            context.SetOutput("item", item);
            context.SetOutput("index", Value.FromNumber(state.Position + 1));
            return ExecResult.Enter("body", $"Элемент {state.Position + 1} из {state.Items.Length}: {item}");
        }

        context.SetState(null);
        return ExecResult.Next("done", $"Перебор завершён: {state.Items.Length} элементов");
    }
}

// ───────────────────────────── Память ─────────────────────────────

public sealed class GetVariableNode : NodeDefinition
{
    public override string TypeId => "get-var";
    public override string Label => "Переменная";
    public override string Category => NodeCategories.Memory;
    public override string Description => "Отдаёт текущее значение переменной.";
    public override IReadOnlyList<ParamDef> Params { get; } = [ParamDef.Variable()];
    public override IReadOnlyList<PortDef> Ports { get; } = [Port.DataOut("value", "значение")];

    public override Value Evaluate(NodeContext context, string outputPort)
        => context.GetVariable(context.Param("variable"));
}

public sealed class SetVariableNode : NodeDefinition
{
    public override string TypeId => "set-var";
    public override string Label => "Записать";
    public override string Category => NodeCategories.Memory;
    public override string Description => "Записывает значение в переменную.";
    public override IReadOnlyList<ParamDef> Params { get; } = [ParamDef.Variable()];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.ExecOut("next", "далее"),
        Port.Inline("value", "значение", DataType.Any, "0"),
    ];

    public override ExecResult Execute(NodeContext context)
    {
        var name = context.Param("variable").Trim();
        var value = context.Input("value");
        context.SetVariable(name, value);
        return ExecResult.Next("next", $"{name} = {value}");
    }
}

/// <summary>Короткая запись для «прочитать — посчитать — записать»: увеличить, уменьшить, умножить, дописать.</summary>
public sealed class ChangeVariableNode : NodeDefinition
{
    public override string TypeId => "change-var";
    public override string Label => "Изменить";
    public override string Category => NodeCategories.Memory;
    public override string Description => "Увеличивает, уменьшает, умножает переменную или дописывает к ней текст.";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Variable(),
        ParamDef.Choice("op", "Действие",
            new ParamChoice("add", "увеличить на"),
            new ParamChoice("sub", "уменьшить на"),
            new ParamChoice("mul", "умножить на"),
            new ParamChoice("append", "дописать")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.ExecIn(),
        Port.ExecOut("next", "далее"),
        Port.Inline("value", "на", DataType.Any, "1"),
    ];

    public override ExecResult Execute(NodeContext context)
    {
        var name = context.Param("variable").Trim();
        var op = context.Param("op");
        var current = context.GetVariable(name);
        var operand = context.Input("value");

        Value next;
        if (op == "append")
        {
            next = Value.FromText(ValueOps.Concat(current, operand));
        }
        else
        {
            if (!current.TryGetNumber(out var left))
                throw context.Error($"В переменной «{name}» не число («{current}»), её нельзя изменить арифметически.");
            if (!operand.TryGetNumber(out var right))
                throw context.Error($"Изменить можно только на число, а получено «{operand}».");
            next = Value.FromNumber(op switch
            {
                "add" => left + right,
                "sub" => left - right,
                _ => left * right
            });
        }

        context.SetVariable(name, next);
        return ExecResult.Next("next", $"{name}: {current} → {next}");
    }
}

// ───────────────────────────── Данные ─────────────────────────────

public sealed class ConstantNode : NodeDefinition
{
    public override string TypeId => "const";
    public override string Label => "Значение";
    public override string Category => NodeCategories.Data;
    public override string Description => "Постоянное значение: число, текст, true/false или список [1, 2, 3].";
    public override IReadOnlyList<ParamDef> Params { get; } = [new ParamDef("value", "Значение", ParamType.Value, "0")];
    public override IReadOnlyList<PortDef> Ports { get; } = [Port.DataOut("value", "значение")];

    public override Value Evaluate(NodeContext context, string outputPort) => Value.Parse(context.Param("value"));
}

public sealed class MathNode : NodeDefinition
{
    public override string TypeId => "math";
    public override string Label => "Арифметика";
    public override string Category => NodeCategories.Math;
    public override string Description => "Складывает, вычитает, умножает, делит или находит остаток от деления.";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Choice("op", "Действие",
            new ParamChoice("+", "+"),
            new ParamChoice("-", "−"),
            new ParamChoice("*", "×"),
            new ParamChoice("/", "÷"),
            new ParamChoice("%", "остаток")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("a", "a", DataType.Number, "0"),
        Port.Inline("b", "b", DataType.Number, "1"),
        Port.DataOut("result", "результат", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var a = context.Number("a");
        var b = context.Number("b");
        var op = context.Param("op");
        if (op is "/" or "%" && Math.Abs(b) < double.Epsilon)
            throw context.Error("Делить на ноль нельзя.");
        return Value.FromNumber(op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "/" => a / b,
            _ => a % b
        });
    }
}

public sealed class CompareNode : NodeDefinition
{
    public override string TypeId => "compare";
    public override string Label => "Сравнение";
    public override string Category => NodeCategories.Logic;
    public override string Description => "Сравнивает два значения. Числа — по величине, текст — без учёта регистра.";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Choice("op", "Сравнение",
            new ParamChoice(">", ">"),
            new ParamChoice("<", "<"),
            new ParamChoice(">=", "≥"),
            new ParamChoice("<=", "≤"),
            new ParamChoice("==", "="),
            new ParamChoice("!=", "≠")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("a", "a", DataType.Any, "0"),
        Port.Inline("b", "b", DataType.Any, "0"),
        Port.DataOut("result", "результат", DataType.Bool),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var a = context.Input("a");
        var b = context.Input("b");
        return Value.FromBool(context.Param("op") switch
        {
            "==" => ValueOps.LooseEquals(a, b),
            "!=" => !ValueOps.LooseEquals(a, b),
            ">" => ValueOps.Compare(a, b) > 0,
            "<" => ValueOps.Compare(a, b) < 0,
            ">=" => ValueOps.Compare(a, b) >= 0,
            _ => ValueOps.Compare(a, b) <= 0
        });
    }
}

public sealed class LogicNode : NodeDefinition
{
    public override string TypeId => "logic";
    public override string Label => "И / ИЛИ";
    public override string Category => NodeCategories.Logic;
    public override string Description => "«И» истинно, когда истинны оба входа; «ИЛИ» — когда истинен хотя бы один.";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Choice("op", "Операция", new ParamChoice("and", "И"), new ParamChoice("or", "ИЛИ")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("a", "a", DataType.Bool, "false"),
        Port.Inline("b", "b", DataType.Bool, "false"),
        Port.DataOut("result", "результат", DataType.Bool),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var a = context.Bool("a");
        var b = context.Bool("b");
        return Value.FromBool(context.Param("op") == "or" ? a || b : a && b);
    }
}

public sealed class NotNode : NodeDefinition
{
    public override string TypeId => "not";
    public override string Label => "НЕ";
    public override string Category => NodeCategories.Logic;
    public override string Description => "Меняет истину на ложь и наоборот.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataIn("value", "значение", DataType.Bool),
        Port.DataOut("result", "результат", DataType.Bool),
    ];

    public override Value Evaluate(NodeContext context, string outputPort) => Value.FromBool(!context.Bool("value"));
}

public sealed class IsTypeNode : NodeDefinition
{
    public override string TypeId => "is-type";
    public override string Label => "Проверка типа";
    public override string Category => NodeCategories.Logic;
    public override string Description => "Проверяет, является ли значение числом, текстом, логическим значением или списком.";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Choice("type", "Тип",
            new ParamChoice("number", "число"),
            new ParamChoice("text", "текст"),
            new ParamChoice("bool", "логическое"),
            new ParamChoice("list", "список")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataIn("value", "значение"),
        Port.DataOut("result", "результат", DataType.Bool),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var value = context.Input("value");
        return Value.FromBool(context.Param("type") switch
        {
            "number" => value.IsNumber,
            "text" => value.IsText,
            "bool" => value.IsBool,
            _ => value.IsList
        });
    }
}

public sealed class TextCheckNode : NodeDefinition
{
    public override string TypeId => "text-check";
    public override string Label => "Поиск в тексте";
    public override string Category => NodeCategories.Text;
    public override string Description => "Проверяет, содержит ли текст фрагмент, начинается или заканчивается им (без учёта регистра).";

    public override IReadOnlyList<ParamDef> Params { get; } =
    [
        ParamDef.Choice("op", "Проверка",
            new ParamChoice("contains", "содержит"),
            new ParamChoice("starts", "начинается с"),
            new ParamChoice("ends", "заканчивается на")),
    ];

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("text", "текст", DataType.Text, ""),
        Port.Inline("fragment", "фрагмент", DataType.Text, ""),
        Port.DataOut("result", "результат", DataType.Bool),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var text = context.Text("text");
        var fragment = context.Text("fragment");
        return Value.FromBool(context.Param("op") switch
        {
            "starts" => text.StartsWith(fragment, StringComparison.OrdinalIgnoreCase),
            "ends" => text.EndsWith(fragment, StringComparison.OrdinalIgnoreCase),
            _ => text.Contains(fragment, StringComparison.OrdinalIgnoreCase)
        });
    }
}

public sealed class JoinNode : NodeDefinition
{
    public override string TypeId => "join";
    public override string Label => "Склеить текст";
    public override string Category => NodeCategories.Text;
    public override string Description => "Соединяет два значения в один текст.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("a", "a", DataType.Text, ""),
        Port.Inline("b", "b", DataType.Text, ""),
        Port.DataOut("result", "результат", DataType.Text),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
        => Value.FromText(context.Text("a") + context.Text("b"));
}

public sealed class LengthNode : NodeDefinition
{
    public override string TypeId => "length";
    public override string Label => "Длина";
    public override string Category => NodeCategories.Text;
    public override string Description => "Число символов в тексте или элементов в списке.";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataIn("value", "значение"),
        Port.DataOut("result", "длина", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var value = context.Input("value");
        return Value.FromNumber(value.IsList ? value.Items.Length : value.ToString().Length);
    }
}

public sealed class ListItemNode : NodeDefinition
{
    public override string TypeId => "list-item";
    public override string Label => "Элемент списка";
    public override string Category => NodeCategories.List;
    public override string Description => "Берёт элемент списка по номеру (нумерация с 1).";

    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataIn("list", "список", DataType.List),
        Port.Inline("index", "номер", DataType.Number, "1"),
        Port.DataOut("item", "элемент"),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var items = context.Input("list").Items;
        var index = context.Number("index");
        if (index % 1 != 0 || index < 1 || index > items.Length)
            throw context.Error($"В списке {items.Length} элементов, номера {Value.FormatNumber(index)} в нём нет.");
        return items[(int)index - 1];
    }
}
