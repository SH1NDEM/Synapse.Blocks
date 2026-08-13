using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class ProgressStore(IJSRuntime js, CampaignModeStore campaignModeStore)
{
    public async Task<HashSet<Guid>> LoadAsync()
    {
        try
        {
            var key = CampaignModeStore.Info(await campaignModeStore.GetAsync()).ProgressStorageKey;
            var json = await js.InvokeAsync<string?>("localStorage.getItem", key);
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
            var key = CampaignModeStore.Info(await campaignModeStore.GetAsync()).ProgressStorageKey;
            await js.InvokeVoidAsync(
                "localStorage.setItem",
                key,
                JsonSerializer.Serialize(completed, AppJsonSerializerContext.Default.HashSetGuid));
        }
        catch (JSException)
        {
            // Прогресс текущей сессии не должен останавливать игру при переполненном хранилище.
        }
    }

    public async Task ResetAsync()
    {
        var key = CampaignModeStore.Info(await campaignModeStore.GetAsync()).ProgressStorageKey;
        await js.InvokeVoidAsync("localStorage.removeItem", key);
    }
}
