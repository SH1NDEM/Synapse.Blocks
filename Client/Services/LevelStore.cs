using System.Text.Json;
using System.Net.Http.Json;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class LevelStore(HttpClient http)
{
    private string? _editorETag;
    private sealed record CachedCampaign(string ETag, List<LevelDefinition> Levels, DateTimeOffset CheckedAt);
    private CachedCampaign? _gameCache, _editorCache;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);
    public Task<List<LevelDefinition>> LoadGameAsync(bool force = false)
    {
        return LoadAsync("api/levels", force: force);
    }

    public Task<List<LevelDefinition>> LoadEditorAsync(bool force = false)
    {
        return LoadAsync("api/admin/levels", editor: true, force: force);
    }

    private async Task<List<LevelDefinition>> LoadAsync(string path, bool editor = false, bool force = false)
    {
        var cache = editor ? _editorCache : _gameCache;
        if (force || cache is null || DateTimeOffset.UtcNow - cache.CheckedAt >= CacheLifetime)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (cache is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cache is not null)
                cache = cache with { CheckedAt = DateTimeOffset.UtcNow };
            else
            {
                await EnsureSuccessAsync(response);
                var levels = await response.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.ListLevelDefinition)
                    ?? throw new InvalidOperationException("Не удалось прочитать кампанию с сервера.");
                cache = new(response.Headers.ETag?.ToString() ?? "", Normalize(levels), DateTimeOffset.UtcNow);
            }
            if (editor) _editorCache = cache;
            else _gameCache = cache;
        }
        if (editor) _editorETag = cache.ETag;
        // Pages edit their own copy; the cached campaign stays unchanged.
        return LevelDrafts.Snapshot(cache.Levels);
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
        _gameCache = _editorCache = null;
    }

    public void ClearEditorCache() { _editorCache = null; _editorETag = null; }

    public async Task<string> ExportAsync(IEnumerable<LevelDefinition> levels)
    {
        var copy = LevelDrafts.Snapshot(levels);
        var embedded = new Dictionary<string, string>();
        foreach (var step in copy.SelectMany(level => level.IntroSteps))
        {
            if (!step.MediaUrl.StartsWith("/api/media/", StringComparison.Ordinal)) continue;
            if (!embedded.TryGetValue(step.MediaUrl, out var data))
            {
                using var response = await http.GetAsync(step.MediaUrl);
                response.EnsureSuccessStatusCode();
                var mime = response.Content.Headers.ContentType?.MediaType ?? "image/gif";
                data = $"data:{mime};base64,{Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync())}";
                embedded[step.MediaUrl] = data;
            }
            step.MediaUrl = data;
        }
        return Export(copy);
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
