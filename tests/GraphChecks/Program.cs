using System.Text;
using Synapse.Blocks.Graph;
using Synapse.Blocks.Graph.Nodes;
using Synapse.Blocks.Models;

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
    Equal("5", result.Steps[0].Outputs["данные"]);
    Equal("8", result.Steps[1].Inputs["значение"]);
    Equal("8", result.Steps[1].Variables["A"]);
    Equal("8", result.Steps[2].Inputs["результат"]);
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

Console.WriteLine();
Console.WriteLine($"Итого: {passed} пройдено, {failed} с ошибками.");
return failed == 0 ? 0 : 1;

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
