using System.Text.Json;
using Microsoft.JSInterop;

namespace Synapse.Blocks.Services;

public sealed class ProgressStore(IJSRuntime js)
{
    private const string StorageKey = "synapse-csharp-progress-v1";

    public async Task<HashSet<Guid>> LoadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<HashSet<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task CompleteAsync(Guid levelId)
    {
        var completed = await LoadAsync();
        completed.Add(levelId);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(completed));
    }

    public async Task ResetAsync()
        => await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
}
