namespace Synapse.Blocks.Services;

/// <summary>
/// Подробные описания заданий игр: Markdown-файлы в wwwroot/content/games/{игра}/{номер}.md.
/// Их можно править как обычный текст, не пересобирая игру.
/// </summary>
public sealed class GameTextStore(HttpClient http)
{
    private readonly Dictionary<string, string> _cache = [];

    /// <summary>Текст описания или пустая строка, если файла нет.</summary>
    public async Task<string> LoadAsync(string game, int order)
    {
        var path = $"content/games/{game}/{order:00}.md";
        if (_cache.TryGetValue(path, out var cached))
            return cached;

        string text;
        try
        {
            text = await http.GetStringAsync(path);
            // Сервер разработки на неизвестный адрес отдаёт страницу приложения — это не описание.
            if (text.TrimStart().StartsWith('<'))
                text = "";
        }
        catch (HttpRequestException)
        {
            text = "";
        }
        return _cache[path] = text;
    }
}
