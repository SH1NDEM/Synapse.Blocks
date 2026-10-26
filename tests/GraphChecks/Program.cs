using System.Text;
using Synapse.Blocks.Graph;
using Synapse.Blocks.Graph.Nodes;
using Synapse.Blocks.Models;
using Synapse.Blocks.Games.Station;
using Synapse.Blocks.Games.Network;

// Проверки ядра графа: значения, блоки, интерпретатор, валидатор и правила уровня.
// Запуск: dotnet run --project tests/GraphChecks

Console.OutputEncoding = Encoding.UTF8;
var runner = new GraphRunner();
var passed = 0;
var failed = 0;

void Test(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  ✓ {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"  ✗ {name}: {exception.Message}");
    }
}

void Cases(GraphProgram program, params (string Input, string Expected)[] cases)
{
    foreach (var (input, expected) in cases)
    {
        var result = runner.Run(program, input);
        if (!result.Success)
            throw new Exception($"вход «{input}»: ошибка «{result.Error}»");
        if (!Value.MatchesExpected(result.Output, expected))
            throw new Exception($"вход «{input}»: ожидалось «{expected}», получено «{result.Output}»");
    }
}

GraphExecution Fails(GraphProgram program, string input, string messagePart, GraphRunner? customRunner = null)
{
    var result = (customRunner ?? runner).Run(program, input);
    if (result.Success)
        throw new Exception($"ожидалась ошибка «{messagePart}», а программа вернула «{result.Output}»");
    if (!result.Error.Contains(messagePart, StringComparison.OrdinalIgnoreCase))
        throw new Exception($"ожидалась ошибка с «{messagePart}», получено «{result.Error}»");
    return result;
}

void CheckLevel(GraphProgram program, string[] allowed, GraphRule[] rules, params (string Input, string Expected)[] cases)
{
    var level = new LevelDefinition { AllowedNodes = [.. allowed], Rules = [.. rules] };
    foreach (var node in program.Nodes)
        Check(LevelNodes.Allows(level, NodeRegistry.Default.Get(node.Type)), $"блок «{node.Type}» недоступен в уровне");
    foreach (var rule in GraphRules.Check(program, level.Rules))
        Check(rule.Passed, $"не выполнено условие: {rule.Description} (сейчас {rule.Actual})");
    var issues = GraphValidator.Validate(program).Where(issue => issue.Severity == IssueSeverity.Error).ToList();
    Check(issues.Count == 0, issues.FirstOrDefault()?.Message ?? "");
    Cases(program, cases);
}

static void Equal<T>(T expected, T actual, string what = "")
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what} ожидалось «{expected}», получено «{actual}»");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Значения");

Test("разбор и форматирование", () =>
{
    Equal(ValueKind.Number, Value.Parse("5").Kind);
    Equal("5.5", Value.Parse("5.50").ToString());
    Equal("-3", Value.Parse(" -3.0 ").ToString());
    Equal(ValueKind.Bool, Value.Parse("true").Kind);
    Equal(ValueKind.Text, Value.Parse("привет").Kind);
    Equal("7", Value.Parse("\"7\"").ToString());
    Equal(ValueKind.Text, Value.Parse("\"7\"").Kind);
    Equal("", Value.Parse("").ToString());
    Equal(ValueKind.Null, Value.Parse("null").Kind);
    var list = Value.Parse("[1, \"a\", [true]]");
    Equal(ValueKind.List, list.Kind);
    Equal(3, list.Items.Length);
    Equal("[1,\"a\",[true]]", list.ToString());
    Equal(ValueKind.Text, Value.Parse("[не json").Kind);
});

Test("сравнение с ответом теста", () =>
{
    Check(Value.MatchesExpected(Value.FromNumber(7), "007"), "7 == 007");
    Check(Value.MatchesExpected(Value.FromText("7"), "7"), "текст 7 == 7");
    Check(Value.MatchesExpected(Value.FromNumber(2.5), "2.50"), "2.5 == 2.50");
    Check(!Value.MatchesExpected(Value.FromText("Да"), "да"), "ответ чувствителен к регистру");
});

Test("нестрогое сравнение значений", () =>
{
    Check(ValueOps.LooseEquals(Value.FromText("5"), Value.FromNumber(5)), "«5» == 5");
    Check(ValueOps.LooseEquals(Value.FromText("Ключ"), Value.FromText("ключ")), "регистр не важен");
    Check(ValueOps.Compare(Value.FromNumber(10), Value.FromNumber(9)) > 0, "10 > 9 как числа");
    Check(ValueOps.Compare(Value.FromText("б"), Value.FromText("А")) > 0, "б > А по алфавиту");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Реестр");

Test("встроенные блоки регистрируются", () =>
{
    var registry = NodeRegistry.CreateDefault();
    Check(registry.All.Count >= 19, $"мало блоков: {registry.All.Count}");
    Check(registry.TryGet("REPEAT", out _), "поиск без учёта регистра");
    Check(registry.Get("math").IsPure, "math — чистый блок");
    Check(!registry.Get("repeat").IsPure, "repeat — блок исполнения");
});

Test("повторная регистрация и конфликт Id запрещены", () =>
{
    var registry = NodeRegistry.CreateDefault();
    var duplicate = false;
    try { registry.Register(new MathNode()); } catch (ArgumentException) { duplicate = true; }
    Check(duplicate, "повтор TypeId должен бросать исключение");

    var clash = false;
    try { new NodeRegistry().Register(new ClashingNode()); } catch (ArgumentException) { clash = true; }
    Check(clash, "совпадение Id входа и настройки должно бросать исключение");
});

Test("свой блок подключается без изменений ядра", () =>
{
    var registry = NodeRegistry.CreateDefault().Register(new SquareNode());
    var b = new GraphBuilder();
    var start = b.Node("start");
    var square = b.Node("square");
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", square, "x").Data(square, "result", output, "value");
    var result = new GraphRunner(registry).Run(b.Program, "9");
    Check(result.Success, result.Error);
    Equal("81", result.Output.ToString());
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Уровни кампании на новых блоках");

Test("Передача сигнала: вход → выход", () => Cases(Passthrough(), ("6", "6"), ("15", "15"), ("0", "0"), ("текст", "текст")));

Test("Прибавить 2", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var add = b.Node("math", ("op", "+"), ("b", "2"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", add, "a").Data(add, "result", output, "value");
    Cases(b.Program, ("5", "7"), ("-2", "0"), ("0.5", "2.5"));
});

Test("Билет на карусель: ≥ 7 → да, иначе нет", () =>
    Cases(Carousel(), ("6", "нет"), ("7", "да"), ("8", "да"), ("-1", "нет")));

Test("Стабилизация сигнала: модуль числа", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if");
    var isNegative = b.Node("compare", ("op", "<"), ("b", "0"));
    var negate = b.Node("math", ("op", "*"), ("b", "-1"));
    var outNegated = b.Node("output");
    var outSame = b.Node("output");
    b.Exec(start, check)
        .Data(start, "value", isNegative, "a").Data(isNegative, "result", check, "condition")
        .Exec(check, "true", outNegated).Data(start, "value", negate, "a").Data(negate, "result", outNegated, "value")
        .Exec(check, "false", outSame).Data(start, "value", outSame, "value");
    Cases(b.Program, ("1", "1"), ("-5", "5"), ("0", "0"));
});

Test("Три восклицания: цикл с переменной", () =>
{
    var b = new GraphBuilder().Variable("S", "");
    var start = b.Node("start");
    var store = b.Node("set-var", ("variable", "S"));
    var loop = b.Node("repeat", ("count", "3"));
    var append = b.Node("change-var", ("variable", "S"), ("op", "append"), ("value", "!"));
    var read = b.Node("get-var", ("variable", "S"));
    var output = b.Node("output");
    b.Exec(start, store).Data(start, "value", store, "value")
        .Exec(store, loop)
        .Exec(loop, "body", append)
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    Cases(b.Program, ("ура", "ура!!!"), ("привет", "привет!!!"), ("да", "да!!!"));
});

Test("Шлюз телеметрии: типы, вложенные условия, несколько выходов", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var ifNumber = b.Node("if");
    var isNumber = b.Node("is-type", ("type", "number"));
    var ifNegative = b.Node("if");
    var isNegative = b.Node("compare", ("op", "<"), ("b", "0"));
    var zero = b.Node("const", ("value", "0"));
    var outZero = b.Node("output");
    var doubled = b.Node("math", ("op", "*"), ("b", "2"));
    var outDoubled = b.Node("output");
    var ifError = b.Node("if");
    var hasError = b.Node("text-check", ("op", "contains"), ("fragment", "ERROR"));
    var alert = b.Node("const", ("value", "ALERT"));
    var outAlert = b.Node("output");
    var length = b.Node("length");
    var outLength = b.Node("output");

    b.Exec(start, ifNumber)
        .Data(start, "value", isNumber, "value").Data(isNumber, "result", ifNumber, "condition")
        .Exec(ifNumber, "true", ifNegative)
        .Data(start, "value", isNegative, "a").Data(isNegative, "result", ifNegative, "condition")
        .Exec(ifNegative, "true", outZero).Data(zero, "value", outZero, "value")
        .Exec(ifNegative, "false", outDoubled).Data(start, "value", doubled, "a").Data(doubled, "result", outDoubled, "value")
        .Exec(ifNumber, "false", ifError)
        .Data(start, "value", hasError, "text").Data(hasError, "result", ifError, "condition")
        .Exec(ifError, "true", outAlert).Data(alert, "value", outAlert, "value")
        .Exec(ifError, "false", outLength).Data(start, "value", length, "value").Data(length, "result", outLength, "value");

    Cases(b.Program, ("7", "14"), ("-4", "0"), ("0", "0"), ("hello", "5"), ("disk error found", "ALERT"));
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Уровни кампании после перевода (доступные блоки и условия)");

string[] loopNodes = ["start", "output", "repeat", "get-var", "set-var", "change-var", "const", "math", "join", "length"];
string[] conditionNodes = ["start", "output", "if", "const", "math", "compare", "is-type", "text-check", "join", "length"];

Test("F. Три шага вперёд: +1 три раза", () =>
{
    var (program, _) = AccumulateInLoop(count: "3", ("add", "1"));
    CheckLevel(program, loopNodes, [GraphRule.Min("repeat"), GraphRule.MinInScope("category:memory", 1), GraphRule.Max("math", 1)],
        ("0", "3"), ("2", "5"), ("5", "8"), ("10", "13"));
});

Test("L. Столько же шагов: +1 столько раз, сколько на входе", () =>
{
    var (program, _) = AccumulateInLoop(count: null, ("add", "1"));
    CheckLevel(program, loopNodes, [GraphRule.Min("repeat"), GraphRule.MinInScope("category:memory", 1), GraphRule.Max("math", 1)],
        ("1", "2"), ("2", "4"), ("5", "10"), ("8", "16"));
});

Test("Импульсный каскад: ×2, затем +1, три раза", () =>
{
    var (program, _) = AccumulateInLoop(count: "3", ("mul", "2"), ("add", "1"));
    CheckLevel(program, loopNodes, [GraphRule.Min("repeat"), GraphRule.MinInScope("category:memory", 2), GraphRule.Max("math", 2)],
        ("1", "15"), ("2", "23"), ("0", "7"), ("-1", "-1"));
});

Test("Степени двойки: 2ⁿ через переменную", () =>
    CheckLevel(PowerOfTwo(), loopNodes, [], ("1", "2"), ("4", "16"), ("10", "1024"), ("16", "65536")));

Test("Индикатор пакетов: строка из N символов #", () =>
{
    var b = new GraphBuilder().Variable("S", "");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var append = b.Node("change-var", ("variable", "S"), ("op", "append"), ("value", "#"));
    var read = b.Node("get-var", ("variable", "S"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", append)
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    CheckLevel(b.Program, loopNodes,
        [GraphRule.Min("repeat"), GraphRule.Min("category:memory"), GraphRule.MinInScope("category:memory", 1), GraphRule.Max("math", 1)],
        ("1", "#"), ("3", "###"), ("12", "############"));
});

Test("Маска доступа: N раз ×2 и +1, начиная с 0", () =>
{
    var b = new GraphBuilder().Variable("M", "0");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var multiply = b.Node("change-var", ("variable", "M"), ("op", "mul"), ("value", "2"));
    var add = b.Node("change-var", ("variable", "M"), ("op", "add"), ("value", "1"));
    var read = b.Node("get-var", ("variable", "M"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", multiply).Exec(multiply, add)
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    CheckLevel(b.Program, loopNodes,
        [GraphRule.Min("repeat"), GraphRule.Min("category:memory"), GraphRule.MinInScope("category:memory", 2), GraphRule.Max("math", 2)],
        ("0", "0"), ("1", "1"), ("3", "7"), ("10", "1023"));
});

Test("Безопасный диапазон: ограничить числом от −50 до 50", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var ifLow = b.Node("if");
    var isLow = b.Node("compare", ("op", "<"), ("b", "-50"));
    var low = b.Node("const", ("value", "-50"));
    var outLow = b.Node("output");
    var ifHigh = b.Node("if");
    var isHigh = b.Node("compare", ("op", ">"), ("b", "50"));
    var high = b.Node("const", ("value", "50"));
    var outHigh = b.Node("output");
    var outSame = b.Node("output");
    b.Exec(start, ifLow).Data(start, "value", isLow, "a").Data(isLow, "result", ifLow, "condition")
        .Exec(ifLow, "true", outLow).Data(low, "value", outLow, "value")
        .Exec(ifLow, "false", ifHigh).Data(start, "value", isHigh, "a").Data(isHigh, "result", ifHigh, "condition")
        .Exec(ifHigh, "true", outHigh).Data(high, "value", outHigh, "value")
        .Exec(ifHigh, "false", outSame).Data(start, "value", outSame, "value");
    CheckLevel(b.Program, conditionNodes, [GraphRule.Min("if"), GraphRule.Max("math", 2)],
        ("-120", "-50"), ("-50", "-50"), ("-7", "-7"), ("50", "50"), ("88", "50"));
});

Test("Степени двойки 2: число → 2ⁿ, текст → длина", () =>
{
    var b = new GraphBuilder().Variable("P", "1");
    var start = b.Node("start");
    var check = b.Node("if");
    var isNumber = b.Node("is-type", ("type", "number"));
    var loop = b.Node("repeat");
    var multiply = b.Node("change-var", ("variable", "P"), ("op", "mul"), ("value", "2"));
    var read = b.Node("get-var", ("variable", "P"));
    var outPower = b.Node("output");
    var length = b.Node("length");
    var outLength = b.Node("output");
    b.Exec(start, check).Data(start, "value", isNumber, "value").Data(isNumber, "result", check, "condition")
        .Exec(check, "true", loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", multiply)
        .Exec(loop, "done", outPower).Data(read, "value", outPower, "value")
        .Exec(check, "false", outLength).Data(start, "value", length, "value").Data(length, "result", outLength, "value");
    string[] allowed = ["start", "output", "if", "repeat", "get-var", "set-var", "change-var", "const", "math", "compare", "is-type", "text-check", "join", "length"];
    CheckLevel(b.Program, allowed, [], ("1", "2"), ("3", "8"), ("PoL0mK4", "7"), ("P0MoG1Te", "8"), ("16", "65536"));
});

Test("Копилка и проверка: переменная 5 + вход, больше 10 → много", () =>
{
    var b = new GraphBuilder().Variable("A", "5");
    var start = b.Node("start");
    var add = b.Node("change-var", ("variable", "A"), ("op", "add"));
    var check = b.Node("if");
    var read = b.Node("get-var", ("variable", "A"));
    var greater = b.Node("compare", ("op", ">"), ("b", "10"));
    var many = b.Node("const", ("value", "много"));
    var normal = b.Node("const", ("value", "нормально"));
    var outMany = b.Node("output");
    var outNormal = b.Node("output");
    b.Exec(start, add).Data(start, "value", add, "value")
        .Exec(add, check).Data(read, "value", greater, "a").Data(greater, "result", check, "condition")
        .Exec(check, "true", outMany).Data(many, "value", outMany, "value")
        .Exec(check, "false", outNormal).Data(normal, "value", outNormal, "value");
    string[] allowed = ["start", "output", "if", "get-var", "set-var", "change-var", "const", "math", "compare", "is-type", "text-check", "join", "length"];
    CheckLevel(b.Program, allowed, [GraphRule.Min("if"), GraphRule.Min("category:memory"), GraphRule.Max("math", 2)],
        ("0", "нормально"), ("5", "нормально"), ("6", "много"), ("9", "много"));
});

Test("недоступный блок обнаруживается", () =>
{
    var level = new LevelDefinition { AllowedNodes = ["start", "output", "math"] };
    var registry = NodeRegistry.Default;
    Check(LevelNodes.Allows(level, registry.Get("start")), "«Вход» доступен всегда");
    Check(LevelNodes.Allows(level, registry.Get("math")), "math разрешён");
    Check(!LevelNodes.Allows(level, registry.Get("repeat")), "repeat не разрешён");
    level.AllowedNodes = ["category:logic"];
    Check(LevelNodes.Allows(level, registry.Get("compare")), "категория разрешает compare");
    Check(LevelNodes.Allows(level, registry.Get("output")), "«Выход» доступен всегда");
    Check(LevelNodes.IsKnownTarget("category:math", registry) && !LevelNodes.IsKnownTarget("category:magic", registry), "проверка категорий");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Циклы");

Test("сумма 1..N через номер повтора", () =>
{
    var b = new GraphBuilder().Variable("sum", "0");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var add = b.Node("change-var", ("variable", "sum"), ("op", "add"));
    var read = b.Node("get-var", ("variable", "sum"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", add).Data(loop, "index", add, "value")
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    Cases(b.Program, ("4", "10"), ("1", "1"), ("0", "0"), ("100", "5050"));
});

Test("вложенные циклы: 3 × 4 = 12", () =>
{
    var b = new GraphBuilder().Variable("count", "0");
    var start = b.Node("start");
    var outer = b.Node("repeat", ("count", "3"));
    var inner = b.Node("repeat", ("count", "4"));
    var increment = b.Node("change-var", ("variable", "count"), ("op", "add"), ("value", "1"));
    var read = b.Node("get-var", ("variable", "count"));
    var output = b.Node("output");
    // Выход «после цикла» внутреннего цикла не подключён: тело внешнего цикла на этом кончается.
    b.Exec(start, outer)
        .Exec(outer, "body", inner)
        .Exec(inner, "body", increment)
        .Exec(outer, "done", output).Data(read, "value", output, "value");
    Cases(b.Program, ("0", "12"));
});

Test("тело цикла из нескольких блоков с условием внутри", () =>
{
    // Считаем чётные номера от 1 до N.
    var b = new GraphBuilder().Variable("even", "0");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var check = b.Node("if");
    var remainder = b.Node("math", ("op", "%"), ("b", "2"));
    var isEven = b.Node("compare", ("op", "=="), ("b", "0"));
    var increment = b.Node("change-var", ("variable", "even"), ("op", "add"), ("value", "1"));
    var read = b.Node("get-var", ("variable", "even"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", check)
        .Data(loop, "index", remainder, "a").Data(remainder, "result", isEven, "a").Data(isEven, "result", check, "condition")
        .Exec(check, "true", increment)
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    Cases(b.Program, ("10", "5"), ("7", "3"), ("1", "0"));
});

Test("цикл «пока»", () =>
{
    var b = new GraphBuilder().Variable("n", "0").Variable("k", "0");
    var start = b.Node("start");
    var store = b.Node("set-var", ("variable", "n"));
    var loop = b.Node("while");
    var readN = b.Node("get-var", ("variable", "n"));
    var positive = b.Node("compare", ("op", ">"), ("b", "0"));
    var decrease = b.Node("change-var", ("variable", "n"), ("op", "sub"), ("value", "3"));
    var increase = b.Node("change-var", ("variable", "k"), ("op", "add"), ("value", "1"));
    var readK = b.Node("get-var", ("variable", "k"));
    var output = b.Node("output");
    b.Exec(start, store).Data(start, "value", store, "value")
        .Exec(store, loop)
        .Data(readN, "value", positive, "a").Data(positive, "result", loop, "condition")
        .Exec(loop, "body", decrease).Exec(decrease, increase)
        .Exec(loop, "done", output).Data(readK, "value", output, "value");
    Cases(b.Program, ("10", "4"), ("3", "1"), ("0", "0"));
});

Test("перебор списка и элемент по номеру", () =>
{
    var b = new GraphBuilder().Variable("sum", "0");
    var start = b.Node("start");
    var each = b.Node("for-each");
    var add = b.Node("change-var", ("variable", "sum"), ("op", "add"));
    var read = b.Node("get-var", ("variable", "sum"));
    var output = b.Node("output");
    b.Exec(start, each).Data(start, "value", each, "list")
        .Exec(each, "body", add).Data(each, "item", add, "value")
        .Exec(each, "done", output).Data(read, "value", output, "value");
    Cases(b.Program, ("[1, 2, 3]", "6"), ("[]", "0"), ("[10]", "10"));

    var second = new GraphBuilder();
    var s = second.Node("start");
    var item = second.Node("list-item", ("index", "2"));
    var o = second.Node("output");
    second.Exec(s, o).Data(s, "value", item, "list").Data(item, "item", o, "value");
    Cases(second.Program, ("[\"a\", \"b\", \"c\"]", "b"));
    Fails(second.Program, "[1]", "номера 2");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Ошибки и проверки структуры");

Test("нет блока «Вход»", () =>
{
    var b = new GraphBuilder();
    b.Node("output");
    Fails(b.Program, "1", "Нужен блок «Вход»");
});

Test("неизвестный блок", () =>
{
    var program = Passthrough();
    program.Nodes.Add(new GraphNode { Type = "teleport" });
    Fails(program, "1", "Неизвестный блок «teleport»");
});

Test("несовместимые типы проводов", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var compare = b.Node("compare");
    var math = b.Node("math");
    var output = b.Node("output");
    b.Exec(start, output).Data(compare, "result", math, "a").Data(math, "result", output, "value");
    Fails(b.Program, "1", "ждёт число");
});

Test("провод данных во вход исполнения", () =>
{
    var program = Passthrough();
    var start = program.Nodes[0];
    var output = program.Nodes[1];
    program.Links.Add(new GraphLink { FromNode = start.Id, FromPort = "value", ToNode = output.Id, ToPort = "in" });
    Fails(program, "1", "Провод данных нельзя подключить ко входу исполнения");
});

Test("два провода из одного выхода исполнения", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var first = b.Node("output");
    var second = b.Node("output");
    b.Exec(start, first).Exec(start, second).Data(start, "value", first, "value").Data(start, "value", second, "value");
    Fails(b.Program, "1", "несколько проводов исполнения");
});

Test("не подключён обязательный вход", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if");
    var output = b.Node("output");
    b.Exec(start, check).Exec(check, "true", output).Exec(check, "false", output).Data(start, "value", output, "value");
    Fails(b.Program, "1", "Подключите вход «условие»");
});

Test("замкнутые в круг блоки данных", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var first = b.Node("math");
    var second = b.Node("math");
    var output = b.Node("output");
    b.Exec(start, output).Data(first, "result", second, "a").Data(second, "result", first, "a").Data(first, "result", output, "value");
    Fails(b.Program, "1", "замкнуты в круг");
});

Test("необъявленная переменная", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var read = b.Node("get-var", ("variable", "X"));
    var output = b.Node("output");
    b.Exec(start, output).Data(read, "value", output, "value");
    Fails(b.Program, "1", "переменная «X» не объявлена");
});

Test("деление на ноль указывает на блок", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var divide = b.Node("math", ("op", "/"), ("b", "0"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", divide, "a").Data(divide, "result", output, "value");
    var result = Fails(b.Program, "5", "Делить на ноль нельзя");
    Equal(divide.Id, result.ErrorNodeId, "блок с ошибкой:");
});

Test("текст вместо числа", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var add = b.Node("math", ("op", "+"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", add, "a").Data(add, "result", output, "value");
    var result = Fails(b.Program, "abc", "ожидалось число");
    Equal(add.Id, result.ErrorNodeId, "блок с ошибкой:");
});

Test("чтение выхода блока, который ещё не выполнялся", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var output = b.Node("output");
    b.Exec(start, output).Data(loop, "index", output, "value");
    Fails(b.Program, "1", "ещё не выполнялся");
});

Test("ветка обрывается до блока «Выход»", () =>
{
    var b = new GraphBuilder().Variable("A");
    var start = b.Node("start");
    var check = b.Node("if");
    var positive = b.Node("compare", ("op", ">"));
    var store = b.Node("set-var", ("variable", "A"));
    var output = b.Node("output");
    b.Exec(start, check).Data(start, "value", positive, "a").Data(positive, "result", check, "condition")
        .Exec(check, "true", output).Data(start, "value", output, "value")
        .Exec(check, "false", store);
    Cases(b.Program, ("5", "5"));
    Fails(b.Program, "-5", "выход «далее» никуда не подключён");
});

Test("бесконечный цикл останавливается", () =>
{
    var b = new GraphBuilder().Variable("A");
    var start = b.Node("start");
    var loop = b.Node("while");
    var always = b.Node("const", ("value", "true"));
    var body = b.Node("change-var", ("variable", "A"), ("op", "add"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(always, "value", loop, "condition")
        .Exec(loop, "body", body).Exec(loop, "done", output).Data(start, "value", output, "value");
    Fails(b.Program, "1", "безопасный предел", new GraphRunner(maxSteps: 500));
});

Test("неверное число повторов", () =>
{
    var b = new GraphBuilder().Variable("A");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var body = b.Node("change-var", ("variable", "A"), ("op", "add"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", body).Exec(loop, "done", output).Data(start, "value", output, "value");
    Fails(b.Program, "2.5", "целым");
    Fails(b.Program, "-1", "целым");
});

Test("недостижимый блок — предупреждение, а не ошибка", () =>
{
    var program = Passthrough();
    program.Nodes.Add(new GraphNode { Type = "set-var", Params = { ["variable"] = "A" } });
    program.Variables.Add(new GraphVariable { Name = "A" });
    var result = runner.Run(program, "3");
    Check(result.Success, result.Error);
    Check(result.Issues.Any(issue => issue.Severity == IssueSeverity.Warning && issue.Message.Contains("не выполнится")), "нет предупреждения");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Отладка, правила уровня, сериализация");

Test("шаги отладки содержат входы, выходы и переменные", () =>
{
    var b = new GraphBuilder().Variable("A", "1");
    var start = b.Node("start");
    var add = b.Node("math", ("op", "+"), ("b", "3"));
    var store = b.Node("set-var", ("variable", "A"));
    var read = b.Node("get-var", ("variable", "A"));
    var output = b.Node("output");
    b.Exec(start, store).Data(start, "value", add, "a").Data(add, "result", store, "value")
        .Exec(store, output).Data(read, "value", output, "value");
    var result = runner.Run(b.Program, "5");
    Check(result.Success, result.Error);
    Equal(3, result.Steps.Count, "число шагов:");
    Equal("5", result.Steps[0].Outputs["value"]);
    Equal("8", result.Steps[1].Inputs["value"]);
    Equal("8", result.Steps[1].Variables["A"]);
    Equal("8", result.Steps[2].Inputs["value"]);
    Equal(new[] { start.Id, store.Id, output.Id }.Length, result.Visited.Count, "посещено блоков:");
});

Test("правила уровня", () =>
{
    var b = new GraphBuilder().Variable("A");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var body = b.Node("change-var", ("variable", "A"), ("op", "add"));
    var output = b.Node("output");
    b.Exec(start, loop).Exec(loop, "body", body).Exec(loop, "done", output).Data(start, "value", output, "value");

    var results = GraphRules.Check(b.Program,
    [
        GraphRule.Min("repeat"),
        GraphRule.Max("category:math", 0),
        GraphRule.MinInScope("category:memory", 1),
        GraphRule.MinInScope("category:memory", 2),
        GraphRule.Min("if"),
    ]);
    Equal(true, results[0].Passed, "есть цикл:");
    Equal(true, results[1].Passed, "нет математики:");
    Equal(true, results[2].Passed, "1 блок памяти в теле:");
    Equal(false, results[3].Passed, "2 блока памяти в теле:");
    Equal(false, results[4].Passed, "нет условия:");
    Check(results[4].Description.Contains("Условие"), results[4].Description);
});

Test("JSON туда и обратно", () =>
{
    var program = Carousel();
    var json = GraphJson.Serialize(program);
    Check(json.Contains("\"type\":\"compare\""), "camelCase в JSON");
    var restored = GraphJson.Deserialize(json);
    Equal(program.Nodes.Count, restored.Nodes.Count, "блоков:");
    Equal(program.Links.Count, restored.Links.Count, "проводов:");
    Cases(restored, ("6", "нет"), ("7", "да"));
});

Test("прогон тестов уровня", () =>
{
    var tests = new List<LevelTestCase>
    {
        new() { Name = "семь", Input = "7", ExpectedOutput = "да" },
        new() { Name = "шесть", Input = "6", ExpectedOutput = "да" },
    };
    var results = runner.RunTests(Carousel(), tests);
    Equal(true, results[0].Passed, "первый тест:");
    Equal(false, results[1].Passed, "второй тест:");
    Equal("нет", results[1].Actual);
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Если");

Test("«Если» выбирает ветку по сравнению", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if-compare", ("op", ">="), ("b", "7"));
    var yes = b.Node("output");
    var no = b.Node("output");
    var yesValue = b.Node("const", ("value", "да"));
    var noValue = b.Node("const", ("value", "нет"));
    b.Exec(start, check).Exec(check, "true", yes).Exec(check, "false", no)
        .Data(start, "value", check, "a").Data(yesValue, "value", yes, "value").Data(noValue, "value", no, "value");
    Cases(b.Program, ("7", "да"), ("12", "да"), ("6", "нет"), ("-1", "нет"));
});

Test("«Если» сравнивает текст без учёта регистра", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if-compare", ("op", "=="), ("b", "запад"));
    var yes = b.Node("output");
    var no = b.Node("output");
    var one = b.Node("const", ("value", "1"));
    var two = b.Node("const", ("value", "2"));
    b.Exec(start, check).Exec(check, "true", yes).Exec(check, "false", no)
        .Data(start, "value", check, "a").Data(one, "value", yes, "value").Data(two, "value", no, "value");
    Cases(b.Program, ("Запад", "1"), ("восток", "2"));
});

Test("«Если» засчитывается вместо «Условия»", () =>
{
    var registry = NodeRegistry.Default;
    var level = new LevelDefinition { AllowedNodes = ["start", "output", "if"] };
    Check(LevelNodes.Allows(level, registry.Get("if-compare")), "«Если» должно быть доступно там, где есть «Условие»");
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if-compare");
    var yes = b.Node("output");
    var no = b.Node("output");
    b.Exec(start, check).Exec(check, "true", yes).Exec(check, "false", no).Data(start, "value", yes, "value").Data(start, "value", no, "value");
    var results = GraphRules.Check(b.Program, [GraphRule.Min("if")]);
    Check(results[0].Passed, "правило «нужно условие» не засчитало «Если»");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Свои блоки");

Test("новый блок возвращает вход", () =>
{
    var b = new GraphBuilder();
    var block = CustomBlocks.Create("Повтор");
    b.Program.CustomBlocks.Add(block);
    var start = b.Node("start");
    var call = b.Node(block.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", call, "i1").Data(call, "o1", output, "value");
    Cases(b.Program, ("5", "5"), ("привет", "привет"));
});

Test("блок «удвоить и прибавить» вызывается дважды", () =>
{
    var b = new GraphBuilder();
    var block = DoubleAndAdd(b.Program);
    var start = b.Node("start");
    var first = b.Node(block.Id, ("i2", "1"));
    var second = b.Node(block.Id, ("i2", "10"));
    var output = b.Node("output");
    b.Exec(start, output)
        .Data(start, "value", first, "i1")
        .Data(first, "o1", second, "i1")
        .Data(second, "o1", output, "value");
    // (5·2+1)·2+10 = 32
    Cases(b.Program, ("5", "32"), ("0", "12"));
});

Test("цикл и переменная внутри блока", () =>
{
    var b = new GraphBuilder();
    var block = Power(b.Program);
    var start = b.Node("start");
    var call = b.Node(block.Id, ("i1", "2"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", call, "i2").Data(call, "o1", output, "value");
    Cases(b.Program, ("0", "1"), ("3", "8"), ("10", "1024"));
});

Test("блок внутри блока и несколько выходов", () =>
{
    var b = new GraphBuilder();
    var inner = DoubleAndAdd(b.Program);
    var outer = CustomBlocks.Create("Два ответа");
    CustomBlocks.AddPort(outer, input: false, "второй", DataType.Number);
    b.Program.CustomBlocks.Add(outer);
    var parameters = outer.Body.Nodes.First(node => node.Type == CustomParamsNode.Id);
    var result = outer.Body.Nodes.First(node => node.Type == CustomReturnNode.Id);
    var call = new GraphNode { Type = inner.Id, Params = { ["i2"] = "0" } };
    outer.Body.Nodes.Add(call);
    outer.Body.Links.Add(new GraphLink { FromNode = parameters.Id, FromPort = "i1", ToNode = call.Id, ToPort = "i1" });
    outer.Body.Links.Add(new GraphLink { FromNode = call.Id, FromPort = "o1", ToNode = result.Id, ToPort = "o2" });

    var start = b.Node("start");
    var use = b.Node(outer.Id);
    var join = b.Node("join");
    var output = b.Node("output");
    b.Exec(start, output)
        .Data(start, "value", use, "i1")
        .Data(use, "o1", join, "a")
        .Data(use, "o2", join, "b")
        .Data(join, "result", output, "value");
    Cases(b.Program, ("4", "48"));
});

Test("ошибки внутри блока видны снаружи", () =>
{
    var b = new GraphBuilder();
    var block = CustomBlocks.Create("Сломанный");
    b.Program.CustomBlocks.Add(block);
    block.Body.Links.RemoveAll(link => link.ToPort == "o1");
    var start = b.Node("start");
    var call = b.Node(block.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(call, "o1", output, "value");
    var issues = GraphValidator.Validate(b.Program);
    var issue = issues.FirstOrDefault(item => item.BlockId == block.Id && item.Severity == IssueSeverity.Error);
    Check(issue is not null && issue.Message.Contains("«Сломанный»"), "нет ошибки блока");
    Fails(b.Program, "1", "Сломанный");
});

Test("блок не может использовать сам себя", () =>
{
    var b = new GraphBuilder();
    var first = CustomBlocks.Create("Первый");
    var second = CustomBlocks.Create("Второй");
    b.Program.CustomBlocks.AddRange([first, second]);
    first.Body.Nodes.Add(new GraphNode { Type = second.Id });
    second.Body.Nodes.Add(new GraphNode { Type = first.Id });
    var start = b.Node("start");
    var call = b.Node(first.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(call, "o1", output, "value");
    Fails(b.Program, "1", "сам себя");
});

Test("«Вход» и «Выход» внутри блока недоступны", () =>
{
    var program = new GraphProgram();
    var block = CustomBlocks.Create("Блок");
    program.CustomBlocks.Add(block);
    var scope = CustomBlocks.Build(program, NodeRegistry.Default).CustomScope!;
    var body = scope.BodyRegistry(block);
    Check(!body.TryGet("start", out _) && !body.TryGet("output", out _), "в теле есть «Вход» или «Выход»");
    Check(body.TryGet("fn-params", out _) && body.TryGet("repeat", out _), "в теле нет нужных блоков");
    Check(!body.TryGet(block.Id, out _), "блок доступен внутри себя");
});

Test("удаление входа убирает его провода", () =>
{
    var b = new GraphBuilder();
    var block = DoubleAndAdd(b.Program);
    var start = b.Node("start");
    var call = b.Node(block.Id, ("i2", "3"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", call, "i1").Data(call, "o1", output, "value");
    CustomBlocks.RemovePort(b.Program, block, "i2");
    Check(!call.Params.ContainsKey("i2"), "осталось значение входа");
    Check(block.Body.Links.All(link => link.FromPort != "i2"), "внутри остался провод");
    // Вход b у «Арифметики» внутри остался без провода и берёт своё значение по умолчанию 1.
    Cases(b.Program, ("5", "11"));
});

Test("правила уровня считают чипы внутри своих блоков", () =>
{
    var b = new GraphBuilder();
    var block = DoubleAndAdd(b.Program);
    var start = b.Node("start");
    var call = b.Node(block.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", call, "i1").Data(call, "o1", output, "value");
    var results = GraphRules.Check(b.Program, [GraphRule.Max("math", 1)]);
    Equal(2, results[0].Actual, "математики внутри блока:");
    Equal(false, results[0].Passed, "ограничение обойдено:");
    var level = new LevelDefinition { AllowedNodes = ["math"] };
    var registry = CustomBlocks.Build(b.Program, NodeRegistry.Default);
    Check(LevelNodes.Allows(level, registry.Get(block.Id)), "свой блок должен быть доступен");
});

Test("свои блоки в JSON", () =>
{
    var b = new GraphBuilder();
    var block = Power(b.Program);
    var start = b.Node("start");
    var call = b.Node(block.Id, ("i1", "3"));
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", call, "i2").Data(call, "o1", output, "value");
    var restored = GraphJson.Deserialize(GraphJson.Serialize(b.Program));
    Equal(1, restored.CustomBlocks.Count, "блоков:");
    Equal(DataType.Number, restored.CustomBlocks[0].Inputs[0].Type, "тип входа:");
    Cases(restored, ("2", "9"));
    var copy = GraphJson.Clone(block);
    Check(copy != block && copy.Id == block.Id && copy.Body.Nodes.Count == block.Body.Nodes.Count, "копия блока");
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Станция");

var stationRunner = new GraphRunner(StationNodes.Registry);

void Solves(int order, GraphProgram program)
{
    var level = StationLevels.All.First(item => item.Order == order);
    var definition = level.AsLevel();
    var registry = CustomBlocks.Build(program, StationNodes.Registry);
    foreach (var node in program.Nodes)
        Check(LevelNodes.Allows(definition, registry.Get(node.Type)), $"блок «{node.Type}» недоступен в задании");
    var run = StationSimulator.Run(level, program, stationRunner);
    Check(run.Success, run.Error);
    Equal(level.Trains.Count, run.Trains.Count, "поездов:");
}

// «Вход → Выход» и «Поезд»; answer строит ответ и возвращает выход данных, который идёт в «Выход».
GraphProgram Dispatcher(Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> answer)
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var train = b.Node(TrainNode.Id);
    var output = b.Node("output");
    var (node, port) = answer(b, train);
    b.Exec(start, output).Data(node, port, output, "value");
    return b.Program;
}

// Условие с двумя «Выходами»: да → yes, нет → no.
GraphProgram Branching(Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> condition, string yes, Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> no)
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var train = b.Node(TrainNode.Id);
    var check = b.Node("if");
    var outYes = b.Node("output");
    var outNo = b.Node("output");
    var (condNode, condPort) = condition(b, train);
    var yesConst = b.Node("const", ("value", yes));
    var (noNode, noPort) = no(b, train);
    b.Exec(start, check).Exec(check, "true", outYes).Exec(check, "false", outNo)
        .Data(condNode, condPort, check, "condition")
        .Data(yesConst, "value", outYes, "value")
        .Data(noNode, noPort, outNo, "value");
    return b.Program;
}

Test("станция 1: первый путь", () => Solves(1, Dispatcher((b, _) => (b.Node("const", ("value", "1")), "value"))));

Test("станция 1: другой путь не принимается", () =>
{
    var level = StationLevels.All[0];
    var run = StationSimulator.Run(level, Dispatcher((b, _) => (b.Node("const", ("value", "2")), "value")), stationRunner);
    Check(!run.Success && run.Error.Contains("путь 1"), run.Error);
});

Test("станция 2: две стороны", () => Solves(2, Branching(
    (b, train) => { var c = b.Node("compare", ("op", "=="), ("b", "запад")); b.Data(train, "from", c, "a"); return (c, "result"); },
    "1",
    (b, _) => (b.Node("const", ("value", "2")), "value"))));

Test("станция 3: длинные составы", () => Solves(3, Branching(
    (b, train) => { var c = b.Node("compare", ("op", ">"), ("b", "6")); b.Data(train, "cars", c, "a"); return (c, "result"); },
    "3",
    (b, _) => (b.Node("const", ("value", "1")), "value"))));

Test("станция 4: свободный путь", () => Solves(4, Dispatcher((b, train) =>
{
    var item = b.Node("list-item", ("index", "1"));
    b.Data(train, "free", item, "list");
    return (item, "item");
})));

Test("станция 4: всегда путь 1 — занято", () =>
{
    var run = StationSimulator.Run(StationLevels.All[3], Dispatcher((b, _) => (b.Node("const", ("value", "1")), "value")), stationRunner);
    Check(!run.Success && run.Error.Contains("занят"), run.Error);
});

Test("станция 5: экспресс", () => Solves(5, Branching(
    (b, train) => { var c = b.Node("compare", ("op", "=="), ("b", "экспресс")); b.Data(train, "kind", c, "a"); return (c, "result"); },
    "1",
    (b, train) =>
    {
        var count = b.Node("length");
        var item = b.Node("list-item");
        b.Data(train, "free", count, "value").Data(train, "free", item, "list").Data(count, "result", item, "index");
        return (item, "item");
    })));

Test("станция: «Поезд» внутри своего блока", () =>
{
    var b = new GraphBuilder();
    var block = CustomBlocks.Create("Первый свободный");
    b.Program.CustomBlocks.Add(block);
    var body = block.Body;
    body.Links.RemoveAll(link => link.ToPort == "o1");
    var train = new GraphNode { Type = TrainNode.Id };
    var item = new GraphNode { Type = "list-item" };
    body.Nodes.AddRange([train, item]);
    var result = body.Nodes.First(node => node.Type == CustomReturnNode.Id);
    body.Links.Add(new GraphLink { FromNode = train.Id, FromPort = "free", ToNode = item.Id, ToPort = "list" });
    body.Links.Add(new GraphLink { FromNode = item.Id, FromPort = "item", ToNode = result.Id, ToPort = "o1" });
    var start = b.Node("start");
    var call = b.Node(block.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(call, "o1", output, "value");
    Solves(4, b.Program);
});

// ─────────────────────────────────────────────────────────────
Console.WriteLine("Связь");

var networkRunner = new GraphRunner(NetworkNodes.Registry);

NetworkRun Route(int order, GraphProgram program)
{
    var level = NetworkLevels.All.First(item => item.Order == order);
    var definition = level.AsLevel();
    var registry = CustomBlocks.Build(program, NetworkNodes.Registry);
    foreach (var node in program.Nodes)
        Check(LevelNodes.Allows(definition, registry.Get(node.Type)), $"блок «{node.Type}» недоступен в задании");
    return NetworkSimulator.Run(level, program, networkRunner);
}

// «Условие» с двумя «Выходами»: condition → да/нет, значения для каждой ветки строят yes и no.
GraphProgram Router(
    Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> condition,
    Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> yes,
    Func<GraphBuilder, GraphNode, (GraphNode Node, string Port)> no)
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var check = b.Node("if");
    var outYes = b.Node("output");
    var outNo = b.Node("output");
    var (c, cp) = condition(b, packet);
    var (y, yp) = yes(b, packet);
    var (n, np) = no(b, packet);
    b.Exec(start, check).Exec(check, "true", outYes).Exec(check, "false", outNo)
        .Data(c, cp, check, "condition").Data(y, yp, outYes, "value").Data(n, np, outNo, "value");
    return b.Program;
}

Test("связь 1: прямой канал", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(packet, "target", output, "value");
    var run = Route(1, b.Program);
    Check(run.Success, run.Error);
    Check(run.Packets.All(result => result.Path.Count == 2), "лишние передачи");
});

Test("связь 2: через центр", () =>
{
    var run = Route(2, Router(
        (b, p) => { var c = b.Node("compare", ("op", "=="), ("b", "Центр")); b.Data(p, "current", c, "a"); return (c, "result"); },
        (b, p) => (p, "target"),
        (b, _) => (b.Node("const", ("value", "Центр")), "value")));
    Check(run.Success, run.Error);
    Equal(3, run.Packets[0].Path.Count, "Север → Центр → Юг:");
});

Test("связь 2: без центра — нет канала", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(packet, "target", output, "value");
    var run = Route(2, b.Program);
    Check(!run.Success && run.Error.Contains("нет канала"), run.Error);
});

Test("связь 3: ретрансляторы", () =>
{
    var run = Route(3, Router(
        (b, p) => { var c = b.Node("compare", ("op", ">")); b.Data(p, "target", c, "a").Data(p, "current", c, "b"); return (c, "result"); },
        (b, p) => { var m = b.Node("math", ("op", "+"), ("b", "1")); b.Data(p, "current", m, "a"); return (m, "result"); },
        (b, p) => { var m = b.Node("math", ("op", "-"), ("b", "1")); b.Data(p, "current", m, "a"); return (m, "result"); }));
    Check(run.Success, run.Error);
    Equal(8, run.Packets[2].Path.Count, "1 → 8:");
});

Test("связь 4: обход обрыва", () =>
{
    var run = Route(4, Router(
        (b, p) =>
        {
            var first = b.Node("list-item", ("index", "1"));
            var c = b.Node("compare", ("op", "!="));
            b.Data(p, "neighbors", first, "list").Data(first, "item", c, "a").Data(p, "previous", c, "b");
            return (c, "result");
        },
        (b, p) => { var first = b.Node("list-item", ("index", "1")); b.Data(p, "neighbors", first, "list"); return (first, "item"); },
        (b, p) =>
        {
            var count = b.Node("length");
            var last = b.Node("list-item");
            b.Data(p, "neighbors", count, "value").Data(p, "neighbors", last, "list").Data(count, "result", last, "index");
            return (last, "item");
        }));
    Check(run.Success, run.Error);
});

Test("связь 3: ретрансляторы через «Если»", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var check = b.Node("if-compare", ("op", ">"));
    var up = b.Node("math", ("op", "+"), ("b", "1"));
    var down = b.Node("math", ("op", "-"), ("b", "1"));
    var outUp = b.Node("output");
    var outDown = b.Node("output");
    b.Exec(start, check).Exec(check, "true", outUp).Exec(check, "false", outDown)
        .Data(packet, "target", check, "a").Data(packet, "current", check, "b")
        .Data(packet, "current", up, "a").Data(packet, "current", down, "a")
        .Data(up, "result", outUp, "value").Data(down, "result", outDown, "value");
    var run = Route(3, b.Program);
    Check(run.Success, run.Error);
});

Test("связь 4: через обрыв не пройти", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var output = b.Node("output");
    b.Exec(start, output).Data(packet, "target", output, "value");
    var run = Route(4, b.Program);
    Check(!run.Success, "пакет не мог дойти напрямую");
});

Test("связь: пакет по кругу теряется", () =>
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var packet = b.Node(PacketNode.Id);
    var first = b.Node("list-item", ("index", "1"));
    var output = b.Node("output");
    b.Exec(start, output).Data(packet, "neighbors", first, "list").Data(first, "item", output, "value");
    var run = Route(4, b.Program);
    Check(!run.Success && run.Error.Contains("по кругу"), run.Error);
});

Console.WriteLine();
Console.WriteLine($"Итого: {passed} пройдено, {failed} с ошибками.");
return failed == 0 ? 0 : 1;

// Блок (x, b) → x·2 + b: два чипа «Арифметика» внутри.
static GraphCustomBlock DoubleAndAdd(GraphProgram program)
{
    var block = CustomBlocks.Create("Удвоить и прибавить");
    block.Inputs[0].Type = DataType.Number;
    CustomBlocks.AddPort(block, input: true, "b", DataType.Number);
    block.Outputs[0].Type = DataType.Number;
    program.CustomBlocks.Add(block);

    var body = block.Body;
    var parameters = body.Nodes.First(node => node.Type == CustomParamsNode.Id);
    var result = body.Nodes.First(node => node.Type == CustomReturnNode.Id);
    body.Links.RemoveAll(link => link.ToPort == "o1");
    var times = new GraphNode { Type = "math", Params = { ["op"] = "*", ["b"] = "2" } };
    var plus = new GraphNode { Type = "math", Params = { ["op"] = "+" } };
    body.Nodes.AddRange([times, plus]);
    body.Links.Add(new GraphLink { FromNode = parameters.Id, FromPort = "i1", ToNode = times.Id, ToPort = "a" });
    body.Links.Add(new GraphLink { FromNode = times.Id, FromPort = "result", ToNode = plus.Id, ToPort = "a" });
    body.Links.Add(new GraphLink { FromNode = parameters.Id, FromPort = "i2", ToNode = plus.Id, ToPort = "b" });
    body.Links.Add(new GraphLink { FromNode = plus.Id, FromPort = "result", ToNode = result.Id, ToPort = "o1" });
    return block;
}

// Блок (основание, степень) → основание^степень: цикл и своя переменная внутри.
static GraphCustomBlock Power(GraphProgram program)
{
    var block = CustomBlocks.Create("Степень");
    block.Inputs[0].Name = "основание";
    block.Inputs[0].Type = DataType.Number;
    CustomBlocks.AddPort(block, input: true, "степень", DataType.Number);
    program.CustomBlocks.Add(block);

    var body = block.Body;
    body.Variables.Add(new GraphVariable { Name = "R", Initial = "1" });
    var parameters = body.Nodes.First(node => node.Type == CustomParamsNode.Id);
    var result = body.Nodes.First(node => node.Type == CustomReturnNode.Id);
    body.Links.Clear();
    var loop = new GraphNode { Type = "repeat" };
    var read = new GraphNode { Type = "get-var", Params = { ["variable"] = "R" } };
    var times = new GraphNode { Type = "math", Params = { ["op"] = "*" } };
    var store = new GraphNode { Type = "set-var", Params = { ["variable"] = "R" } };
    var final = new GraphNode { Type = "get-var", Params = { ["variable"] = "R" } };
    body.Nodes.AddRange([loop, read, times, store, final]);
    void Link(GraphNode from, string fromPort, GraphNode to, string toPort)
        => body.Links.Add(new GraphLink { FromNode = from.Id, FromPort = fromPort, ToNode = to.Id, ToPort = toPort });
    Link(parameters, "next", loop, "in");
    Link(parameters, "i2", loop, "count");
    Link(loop, "body", store, "in");
    Link(read, "value", times, "a");
    Link(parameters, "i1", times, "b");
    Link(times, "result", store, "value");
    Link(loop, "done", result, "in");
    Link(final, "value", result, "o1");
    return block;
}

// ─────────────────────────────────────────────────────────────

static GraphProgram Passthrough()
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var output = b.Node("output");
    b.Exec(start, output).Data(start, "value", output, "value");
    return b.Program;
}

static GraphProgram Carousel()
{
    var b = new GraphBuilder();
    var start = b.Node("start");
    var check = b.Node("if");
    var atLeast = b.Node("compare", ("op", ">="), ("b", "7"));
    var yes = b.Node("const", ("value", "да"));
    var no = b.Node("const", ("value", "нет"));
    var outYes = b.Node("output");
    var outNo = b.Node("output");
    b.Exec(start, check)
        .Data(start, "value", atLeast, "a").Data(atLeast, "result", check, "condition")
        .Exec(check, "true", outYes).Data(yes, "value", outYes, "value")
        .Exec(check, "false", outNo).Data(no, "value", outNo, "value");
    return b.Program;
}

/// <summary>Программа «записать вход в X, повторить действия над X, вернуть X».</summary>
static (GraphProgram Program, GraphNode Loop) AccumulateInLoop(string? count, params (string Op, string Value)[] actions)
{
    var b = new GraphBuilder().Variable("X", "0");
    var start = b.Node("start");
    var store = b.Node("set-var", ("variable", "X"));
    var loop = count is null ? b.Node("repeat") : b.Node("repeat", ("count", count));
    var read = b.Node("get-var", ("variable", "X"));
    var output = b.Node("output");
    b.Exec(start, store).Data(start, "value", store, "value").Exec(store, loop);
    if (count is null) b.Data(start, "value", loop, "count");
    GraphNode? previous = null;
    foreach (var (op, value) in actions)
    {
        var action = b.Node("change-var", ("variable", "X"), ("op", op), ("value", value));
        if (previous is null) b.Exec(loop, "body", action);
        else b.Exec(previous, action);
        previous = action;
    }
    b.Exec(loop, "done", output).Data(read, "value", output, "value");
    return (b.Program, loop);
}

static GraphProgram PowerOfTwo()
{
    var b = new GraphBuilder().Variable("P", "1");
    var start = b.Node("start");
    var loop = b.Node("repeat");
    var multiply = b.Node("change-var", ("variable", "P"), ("op", "mul"), ("value", "2"));
    var read = b.Node("get-var", ("variable", "P"));
    var output = b.Node("output");
    b.Exec(start, loop).Data(start, "value", loop, "count")
        .Exec(loop, "body", multiply)
        .Exec(loop, "done", output).Data(read, "value", output, "value");
    return b.Program;
}

/// <summary>Пример стороннего блока: квадрат числа.</summary>
sealed class SquareNode : NodeDefinition
{
    public override string TypeId => "square";
    public override string Label => "Квадрат";
    public override string Category => NodeCategories.Math;
    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.DataIn("x", "x", DataType.Number),
        Port.DataOut("result", "x²", DataType.Number),
    ];

    public override Value Evaluate(NodeContext context, string outputPort)
    {
        var x = context.Number("x");
        return Value.FromNumber(x * x);
    }
}

/// <summary>Блок с ошибкой описания: вход данных и настройка с одинаковым Id.</summary>
sealed class ClashingNode : NodeDefinition
{
    public override string TypeId => "clash";
    public override string Label => "Конфликт";
    public override IReadOnlyList<PortDef> Ports { get; } =
    [
        Port.Inline("value", "значение", DataType.Any, "0"),
        Port.DataOut("result", "результат"),
    ];
    public override IReadOnlyList<ParamDef> Params { get; } = [new ParamDef("value", "Значение", ParamType.Value)];
}
