using Microsoft.JSInterop;

namespace Synapse.Blocks.Services;

/// <summary>Описывает два независимых набора заданий, доступных на главной странице.</summary>
public enum CampaignMode
{
    Adult,
    Kids
}

/// <summary>Файлы и браузерные ключи, относящиеся к одному набору заданий.</summary>
public sealed record CampaignModeInfo(
    string LevelsFile,
    string ProgressStorageKey,
    string SolutionStoragePrefix,
    string Label,
    string Description);

/// <summary>Хранит выбранную кампанию и не даёт данным двух режимов смешиваться.</summary>
public sealed class CampaignModeStore(IJSRuntime js)
{
    private const string StorageKey = "synapse-campaign-mode-v1";

    public async Task<CampaignMode> GetAsync()
    {
        try
        {
            var saved = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return string.Equals(saved, "kids", StringComparison.OrdinalIgnoreCase)
                ? CampaignMode.Kids
                : CampaignMode.Adult;
        }
        catch
        {
            return CampaignMode.Adult;
        }
    }

    public Task SetAsync(CampaignMode mode)
        => js.InvokeVoidAsync(
            "localStorage.setItem",
            StorageKey,
            mode == CampaignMode.Kids ? "kids" : "adult").AsTask();

    public static CampaignModeInfo Info(CampaignMode mode) => mode switch
    {
        // Старые сохранения обычной кампании остаются доступными.
        CampaignMode.Adult => new(
            "levels-adult.json",
            "synapse-csharp-progress-v1",
            "synapse-solution-v1-",
            "Обычная версия",
            "Задачи с полной логикой, условиями, циклами и памятью."),
        CampaignMode.Kids => new(
            "levels-kids.json",
            "synapse-kids-progress-v1",
            "synapse-kids-solution-v1-",
            "Для детей 1–3 класс",
            "Простые числа, понятные действия и первые повторения."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
