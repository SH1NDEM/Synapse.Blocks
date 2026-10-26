using Microsoft.JSInterop;
using Synapse.Blocks.Graph;

namespace Synapse.Blocks.Services;

/// <summary>
/// Личная библиотека своих блоков на этом устройстве. Блок из решения можно сохранить сюда
/// и потом добавить в схему любого уровня: в решение кладётся независимая копия.
/// </summary>
public sealed class BlockLibraryStore(IJSRuntime js)
{
    private const string Key = "synapse-block-library-v1";

    public async Task<List<GraphCustomBlock>> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            return string.IsNullOrWhiteSpace(json) ? [] : GraphJson.DeserializeBlocks(json);
        }
        catch { return []; }
    }

    /// <summary>Сохраняет копию блока; блок с тем же Id заменяется.</summary>
    public async Task<List<GraphCustomBlock>> SaveAsync(GraphCustomBlock block)
    {
        var library = await LoadAsync();
        library.RemoveAll(item => string.Equals(item.Id, block.Id, StringComparison.OrdinalIgnoreCase));
        library.Add(GraphJson.Clone(block));
        await WriteAsync(library);
        return library;
    }

    public async Task<List<GraphCustomBlock>> RemoveAsync(string id)
    {
        var library = await LoadAsync();
        library.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        await WriteAsync(library);
        return library;
    }

    private async Task WriteAsync(List<GraphCustomBlock> library)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, GraphJson.SerializeBlocks(library));
        }
        catch (JSException) { }
    }
}
