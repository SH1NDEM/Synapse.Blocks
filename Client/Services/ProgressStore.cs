using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class ProgressStore(IJSRuntime js)
{
    private const string StorageKey = "synapse-csharp-progress-v1";

    public async Task<HashSet<Guid>> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(json)) return [];
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.HashSetGuid) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task CompleteAsync(Guid levelId)
    {
        var completed = await LoadAsync();
        completed.Add(levelId);
        try
        {
            await js.InvokeVoidAsync(
                "localStorage.setItem",
                StorageKey,
                JsonSerializer.Serialize(completed, AppJsonSerializerContext.Default.HashSetGuid));
        }
        catch (JSException)
        {
            // Прогресс текущей сессии не должен останавливать игру при переполненном хранилище.
        }
    }

    public async Task ResetAsync()
        => await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
}
