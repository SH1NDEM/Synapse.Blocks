using System.Text.Json;
using System.Net.Http.Json;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class LevelStore(HttpClient http)
{
    private string? _editorETag;
    public Task<List<LevelDefinition>> LoadGameAsync()
    {
        return LoadAsync("api/levels");
    }

    public Task<List<LevelDefinition>> LoadEditorAsync()
    {
        return LoadAsync("api/admin/levels", editor: true);
    }

    private async Task<List<LevelDefinition>> LoadAsync(string path, bool editor = false)
    {
        using var response = await http.GetAsync(path);
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();

        var levels = JsonSerializer.Deserialize(
            json,
            AppJsonSerializerContext.Default.ListLevelDefinition);

        if (levels is null)
            throw new InvalidOperationException("Не удалось прочитать кампанию с сервера.");

        var normalized = Normalize(levels);
        if (editor) _editorETag = response.Headers.ETag?.ToString();
        return normalized;
    }

    public async Task SaveAsync(List<LevelDefinition> levels)
    {
        if (_editorETag is null) throw new InvalidOperationException("Сначала загрузите кампанию с сервера.");
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/admin/levels")
        {
            Content = JsonContent.Create(Normalize(levels), AppJsonSerializerContext.Default.ListLevelDefinition)
        };
        request.Headers.Add("X-Synapse-Request", "1");
        request.Headers.TryAddWithoutValidation("If-Match", _editorETag);
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
        _editorETag = response.Headers.ETag?.ToString();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Сессия истекла. Экспортируйте свои правки и обновите страницу для входа.");
        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
            throw new InvalidOperationException("Кампания слишком большая. Уменьшите размер встроенных изображений.");
        throw new InvalidOperationException("Сервер недоступен или не смог обработать запрос. Попробуйте ещё раз.");
    }

    public string Export(IEnumerable<LevelDefinition> levels)
        => JsonSerializer.Serialize(Normalize(levels.ToList()), AppJsonSerializerContext.Default.ListLevelDefinition);

    
    public List<LevelDefinition> Import(string json)
    {
        var levels = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListLevelDefinition)
            ?? throw new InvalidOperationException("В файле нет списка уровней.");
        if (levels.Count == 0)
            throw new InvalidOperationException("Нужен хотя бы один уровень.");
        return Normalize(levels);
    }

    private static List<LevelDefinition> Normalize(List<LevelDefinition> levels)
    {
        // Нормализация также страхует уровни, импортированные из более старой версии редактора.
        var ordered = levels.OrderBy(level => level.Order).ThenBy(level => level.Title).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var level = ordered[index];
            level.Order = index + 1;
            level.Chapter = Math.Max(1, level.Chapter);
            level.AllowedBlocks = level.AllowedBlocks.Distinct().ToList();
            if (!level.AllowedBlocks.Contains(BlockKind.Input)) level.AllowedBlocks.Insert(0, BlockKind.Input);
            if (!level.AllowedBlocks.Contains(BlockKind.Output)) level.AllowedBlocks.Add(BlockKind.Output);
            level.Tests ??= [];
            level.IntroSteps ??= [];
            foreach (var step in level.IntroSteps)
            {
                step.MediaUrl ??= "";
                if (step.HighlightTarget is IntroHighlightTarget.InputData or IntroHighlightTarget.OutputData)
                    step.HighlightTarget = IntroHighlightTarget.Description;
            }
        }
        return ordered;
    }
}
