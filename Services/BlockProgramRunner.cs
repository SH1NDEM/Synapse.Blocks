using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Services;

/// <summary>
/// Выполняет пользовательский граф блоков без знания конкретного уровня.
/// Уровень поставляет только входы и ожидаемые результаты тестов.
/// </summary>
public sealed partial class BlockProgramRunner
{
    // Защита от случайно собранного бесконечного цикла.
    private const int MaxSteps = 10_000;

    public IReadOnlyList<TestRunResult> RunAll(BlockProgram program, LevelDefinition level)
    {
        return level.Tests.Select(test =>
        {
            var execution = Run(program, test.Input);
            var passed = execution.Success && ValuesEqual(execution.Output, test.ExpectedOutput);
            return new TestRunResult(
                test.Id,
                test.Name,
                passed,
                test.Input,
                test.ExpectedOutput,
                execution.Output,
                execution.Error,
                execution.VisitedNodes);
        }).ToList();
    }

    public ProgramExecution Run(BlockProgram program, string rawInput)
    {
        // Валидируем обязательные границы программы до обхода графа.
        var inputNodes = program.Nodes.Where(node => node.Kind == BlockKind.Input).ToList();
        var outputNodes = program.Nodes.Where(node => node.Kind == BlockKind.Output).ToList();
        if (inputNodes.Count != 1)
            return Fail("В программе должен быть ровно один блок «Вход».");
        if (outputNodes.Count != 1)
            return Fail("В программе должен быть ровно один блок «Выход».");

        var nodes = program.Nodes.ToDictionary(node => node.Id);
        var edges = program.Connections
            .GroupBy(edge => (edge.FromNodeId, edge.Port))
            .ToDictionary(group => group.Key, group => group.Last());
        var duplicateEdge = program.Connections
            .GroupBy(edge => (edge.FromNodeId, edge.Port))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateEdge is not null)
            return Fail("Из одного порта нельзя провести две связи.");

        object? value = ParseValue(rawInput);
        var visited = new List<Guid>();
        var steps = new List<ExecutionStep>();
        // Каждый цикл хранит собственный счётчик, поэтому вложенные маршруты не смешиваются.
        var loopStates = new Dictionary<Guid, (int Total, int Completed)>();
        var current = inputNodes[0];

        for (var step = 0; step < MaxSteps; step++)
        {
            var executing = current;
            var before = FormatValue(value);
            visited.Add(executing.Id);
            switch (executing.Kind)
            {
                case BlockKind.Input:
                    if (!TryNext(executing.Id, OutputPort.Next, out current, out var inputError))
                    {
                        steps.Add(new(executing.Id, executing.Kind, before, before, inputError));
                        return Fail(inputError, visited, steps);
                    }
                    steps.Add(new(executing.Id, executing.Kind, before, before, "Входные данные получены"));
                    break;

                case BlockKind.Operation:
                    if (!TryApplyOperation(value, executing.Config, out value, out var operationError))
                    {
                        steps.Add(new(executing.Id, executing.Kind, before, before, operationError));
                        return Fail(operationError, visited, steps);
                    }
                    steps.Add(new(executing.Id, executing.Kind, before, FormatValue(value), $"Команда: {executing.Config.Trim()}"));
                    if (!TryNext(executing.Id, OutputPort.Next, out current, out var nextError))
                        return Fail(nextError, visited, steps);
                    break;

                case BlockKind.Condition:
                    if (!TryEvaluateCondition(value, executing.Config, out var condition, out var conditionError))
                    {
                        steps.Add(new(executing.Id, executing.Kind, before, before, conditionError));
                        return Fail(conditionError, visited, steps);
                    }
                    var conditionPort = condition ? OutputPort.True : OutputPort.False;
                    steps.Add(new(executing.Id, executing.Kind, before, before, condition ? "Условие выполнено — синяя ветка" : "Условие не выполнено — красная ветка"));
                    if (!TryNext(executing.Id, conditionPort, out current, out var branchError))
                        return Fail(branchError, visited, steps);
                    break;

                case BlockKind.Loop:
                    // Первый вход создаёт счётчик, каждый возврат снизу завершает одну итерацию.
                    if (!loopStates.TryGetValue(executing.Id, out var state))
                    {
                        if (!TryGetLoopCount(value, executing.Config, out var total, out var loopError))
                        {
                            steps.Add(new(executing.Id, executing.Kind, before, before, loopError));
                            return Fail(loopError, visited, steps);
                        }
                        state = (total, 0);
                        loopStates[executing.Id] = state;
                    }
                    else
                    {
                        state = (state.Total, state.Completed + 1);
                        loopStates[executing.Id] = state;
                    }

                    var loopPort = state.Completed < state.Total ? OutputPort.Repeat : OutputPort.Done;
                    if (loopPort == OutputPort.Done)
                        loopStates.Remove(executing.Id);
                    var loopDetail = loopPort == OutputPort.Repeat
                        ? $"Повтор {state.Completed + 1} из {state.Total}"
                        : $"Цикл завершён: {state.Total} повторов";
                    steps.Add(new(executing.Id, executing.Kind, before, before, loopDetail));
                    if (!TryNext(executing.Id, loopPort, out current, out var loopNextError))
                        return Fail(loopNextError, visited, steps);
                    break;

                case BlockKind.Output:
                    steps.Add(new(executing.Id, executing.Kind, before, before, "Результат программы"));
                    return new ProgramExecution(true, before, "", visited, steps);
            }
        }

        return Fail("Программа превысила безопасный предел шагов. Возможно, цикл бесконечный.", visited, steps);

        bool TryNext(Guid from, OutputPort port, out BlockNode next, out string error)
        {
            // Переход выбирается по конкретному выходному порту: обычному, ветке или порту цикла.
            next = null!;
            if (!edges.TryGetValue((from, port), out var edge))
            {
                error = $"Порт «{PortLabel(port)}» не подключён.";
                return false;
            }
            if (!nodes.TryGetValue(edge.ToNodeId, out next!))
            {
                error = "Связь ведёт к удалённому блоку.";
                return false;
            }
            error = "";
            return true;
        }
    }

    private static bool TryApplyOperation(object? input, string expression, out object? output, out string error)
    {
        output = input;
        error = "";
        var command = expression.Trim();
        if (string.IsNullOrWhiteSpace(command) || command.Equals("noop", StringComparison.OrdinalIgnoreCase))
            return true;

        // Пустая команда «Задать значение» намеренно создаёт пустую строку и не сбрасывает режим блока.
        if (command.Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            output = "";
            return true;
        }
        if (command.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            output = ParseValue(command[4..]);
            return true;
        }
        if (command.Equals("append", StringComparison.OrdinalIgnoreCase))
        {
            output = AsText(input);
            return true;
        }
        if (command.StartsWith("append ", StringComparison.OrdinalIgnoreCase))
        {
            output = $"{AsText(input)}{command[7..]}";
            return true;
        }
        if (command.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            output = input switch
            {
                JsonArray array => array.Count,
                JsonObject obj => obj.Count,
                string text => text.Length,
                _ => 1
            };
            return true;
        }

        var match = ArithmeticRegex().Match(command);
        if (!match.Success || !TryNumber(input, out var left) || !TryNumber(match.Groups[2].Value, out var right))
        {
            error = "Неизвестная операция. Примеры: + 2, * 3, set готово, append !, count.";
            return false;
        }

        output = match.Groups[1].Value switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" when Math.Abs(right) > double.Epsilon => left / right,
            "/" => null,
            _ => null
        };
        if (output is null)
        {
            error = "Делить на ноль нельзя.";
            return false;
        }
        return true;
    }

    private static bool TryEvaluateCondition(object? input, string expression, out bool result, out string error)
    {
        result = false;
        error = "";
        var condition = expression.Trim();
        if (string.IsNullOrWhiteSpace(condition))
        {
            error = "В блоке условия не задана проверка.";
            return false;
        }

        foreach (var (prefix, predicate) in new (string Prefix, Func<string, string, bool> Test)[]
        {
            ("contains ", (left, right) => left.Contains(right, StringComparison.OrdinalIgnoreCase)),
            ("starts ", (left, right) => left.StartsWith(right, StringComparison.OrdinalIgnoreCase)),
            ("ends ", (left, right) => left.EndsWith(right, StringComparison.OrdinalIgnoreCase))
        })
        {
            if (!condition.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            result = predicate(AsText(input), condition[prefix.Length..]);
            return true;
        }

        var match = ComparisonRegex().Match(condition);
        if (!match.Success)
        {
            error = "Неизвестное условие. Примеры: > 20, == ключ, contains ошибка.";
            return false;
        }

        var op = match.Groups[1].Value;
        var rightRaw = match.Groups[2].Value.Trim();
        if (TryNumber(input, out var leftNumber) && TryNumber(rightRaw, out var rightNumber))
        {
            result = op switch
            {
                ">" => leftNumber > rightNumber,
                "<" => leftNumber < rightNumber,
                ">=" => leftNumber >= rightNumber,
                "<=" => leftNumber <= rightNumber,
                "==" => Math.Abs(leftNumber - rightNumber) < 0.0000001,
                "!=" => Math.Abs(leftNumber - rightNumber) >= 0.0000001,
                _ => false
            };
            return true;
        }

        var comparison = string.Compare(AsText(input), AsText(ParseValue(rightRaw)), StringComparison.OrdinalIgnoreCase);
        result = op switch
        {
            "==" => comparison == 0,
            "!=" => comparison != 0,
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            _ => false
        };
        return true;
    }

    private static bool TryGetLoopCount(object? value, string config, out int count, out string error)
    {
        error = "";
        var source = config.Trim().Equals("input", StringComparison.OrdinalIgnoreCase)
            || config.Trim().Equals("вход", StringComparison.OrdinalIgnoreCase)
            ? value
            : config;
        if (!TryNumber(source, out var number) || number < 0 || number > 1_000 || number % 1 != 0)
        {
            count = 0;
            error = "Циклу нужно целое число повторов от 0 до 1000 или значение «input».";
            return false;
        }
        count = (int)number;
        return true;
    }

    public static bool ValuesEqual(string actual, string expected)
        => string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);

    public static string Normalize(string raw) => FormatValue(ParseValue(raw));

    private static object? ParseValue(string raw)
    {
        var value = raw.Trim();
        if (string.IsNullOrEmpty(value)) return "";
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            if (bool.TryParse(value, out var boolean)) return boolean;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
            return value.Trim('"');
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) => text,
            JsonNode node => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            double number when Math.Abs(number % 1) < 0.0000001 => ((long)number).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("0.########", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static string AsText(object? value) => FormatValue(value).Trim('"');

    private static bool TryNumber(object? value, out double number)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out number)) return true;
        if (value is double doubleValue) { number = doubleValue; return true; }
        if (value is int intValue) { number = intValue; return true; }
        return double.TryParse(AsText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string PortLabel(OutputPort port) => port switch
    {
        OutputPort.True => "условие выполнено",
        OutputPort.False => "условие не выполнено",
        OutputPort.Repeat => "повтор",
        OutputPort.Done => "выход",
        _ => "выход"
    };

    private static ProgramExecution Fail(
        string error,
        IReadOnlyList<Guid>? visited = null,
        IReadOnlyList<ExecutionStep>? steps = null)
        => new(false, "", error, visited ?? [], steps ?? []);

    [GeneratedRegex(@"^([+\-*/])\s*(.+)$")]
    private static partial Regex ArithmeticRegex();

    [GeneratedRegex(@"^(>=|<=|==|!=|>|<)\s*(.+)$")]
    private static partial Regex ComparisonRegex();
}
