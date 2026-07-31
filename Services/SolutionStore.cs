using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

/// <summary>Хранит черновик графа отдельно для каждого уровня на этом устройстве.</summary>
public sealed class SolutionStore(IJSRuntime js)
{
    private static string Key(Guid levelId) => $"synapse-solution-v1-{levelId:N}";

    public async Task<BlockProgram?> LoadAsync(Guid levelId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key(levelId));
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.BlockProgram);
        }
        catch { return null; }
    }

    public async Task SaveAsync(Guid levelId, BlockProgram program)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key(levelId), JsonSerializer.Serialize(program, AppJsonSerializerContext.Default.BlockProgram));
        }
        catch (JSException) { }
    }

    public async Task RemoveAsync(Guid levelId) => await js.InvokeVoidAsync("localStorage.removeItem", Key(levelId));
}
