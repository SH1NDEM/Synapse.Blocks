using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Components;

/// <summary>Подписи результатов тестов: скрытые проверки не раскрывают данные, пока не провалены.</summary>
public static class TestResultText
{
    public static string Display(string value) => string.IsNullOrEmpty(value) ? "∅ пусто" : value;

    public static bool IsHidden(LevelDefinition level, GraphTestResult result)
        => level.Tests.FirstOrDefault(test => test.Id == result.TestId)?.Hidden == true;

    public static string Title(LevelDefinition level, GraphTestResult result)
    {
        if (!IsHidden(level, result)) return result.Name;
        var index = level.Tests.FindIndex(test => test.Id == result.TestId);
        return $"Скрытая проверка {(index >= 0 ? index + 1 : 0)}";
    }

    public static string Detail(LevelDefinition level, GraphTestResult result)
    {
        if (IsHidden(level, result))
        {
            if (result.Passed) return "скрытая проверка пройдена";
            var values = $"вход: {Display(result.Input)} · ожидалось: {Display(result.Expected)} · получено: {Display(result.Actual)}";
            return string.IsNullOrWhiteSpace(result.Error) ? values : $"{result.Error} · {values}";
        }
        if (result.Passed) return $"получено: {Display(result.Actual)}";
        return !string.IsNullOrWhiteSpace(result.Error)
            ? result.Error
            : $"ожидалось {Display(result.Expected)}, получено {Display(result.Actual)}";
    }
}
