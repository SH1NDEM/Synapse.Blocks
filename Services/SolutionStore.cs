using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

/// <summary>Хранит черновик графа отдельно для каждого уровня на этом устройстве.</summary>
public sealed class SolutionStore(IJSRuntime js, CampaignModeStore campaignModeStore)
{
    private static string Key(string prefix, Guid levelId) => $"{prefix}{levelId:N}";

    private async Task<string> PrefixAsync()
        => CampaignModeStore.Info(await campaignModeStore.GetAsync()).SolutionStoragePrefix;

    public async Task<BlockProgram?> LoadAsync(Guid levelId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key(await PrefixAsync(), levelId));
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.BlockProgram);
        }
        catch { return null; }
    }

    public async Task SaveAsync(Guid levelId, BlockProgram program)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key(await PrefixAsync(), levelId), JsonSerializer.Serialize(program, AppJsonSerializerContext.Default.BlockProgram));
        }
        catch (JSException) { }
    }

    public async Task RemoveAsync(Guid levelId) => await js.InvokeVoidAsync("localStorage.removeItem", Key(await PrefixAsync(), levelId));

    /// <summary>Удаляет черновики всех уровней перед передачей компьютера следующему игроку.</summary>
    public async Task ResetAllAsync()
        => await js.InvokeVoidAsync("synapseStorage.removeByPrefix", await PrefixAsync());
}
