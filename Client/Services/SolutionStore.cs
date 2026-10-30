using Microsoft.JSInterop;
using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Services;

/// <summary>Хранит черновик графа отдельно для каждого уровня на этом устройстве.</summary>
public sealed class SolutionStore(IJSRuntime js)
{
    // v2 — программы из двух видов проводов. Черновики старого формата (v1) не читаются.
    private const string StoragePrefix = "synapse-solution-v2-";
    private const string LegacyPrefix = "synapse-solution-v1-";
    private static string Key(Guid levelId) => $"{StoragePrefix}{levelId:N}";

    public async Task<GraphProgram?> LoadAsync(Guid levelId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key(levelId));
            return string.IsNullOrWhiteSpace(json) ? null : GraphJson.Deserialize(json);
        }
        catch { return null; }
    }

    public async Task SaveAsync(Guid levelId, GraphProgram program)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key(levelId), GraphJson.Serialize(program));
        }
        catch (JSException) { }
    }

    public async Task RemoveAsync(Guid levelId) => await js.InvokeVoidAsync("localStorage.removeItem", Key(levelId));

    /// <summary>Удаляет черновики всех уровней перед передачей компьютера следующему игроку.</summary>
    public async Task ResetAllAsync()
    {
        await js.InvokeVoidAsync("synapseStorage.removeByPrefix", StoragePrefix);
        await js.InvokeVoidAsync("synapseStorage.removeByPrefix", LegacyPrefix);
    }
}
