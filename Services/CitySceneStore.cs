using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class CitySceneStore(HttpClient http, IJSRuntime js)
{
    private const string StorageKey = "synapse-city-scene-v1";

    public async Task<CitySceneDefinition> LoadAsync()
    {
        var local = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (!string.IsNullOrWhiteSpace(local))
        {
            try { return Normalize(Deserialize(local)); }
            catch { await js.InvokeVoidAsync("localStorage.removeItem", StorageKey); }
        }
        return await LoadFileAsync();
    }

    public async Task<CitySceneDefinition> LoadFileAsync()
    {
        var json = await http.GetStringAsync("city-scene.json");
        return Normalize(Deserialize(json));
    }

    public async Task SaveLocalAsync(CitySceneDefinition scene)
        => await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Export(scene));

    public async Task ClearLocalAsync()
        => await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);

    public CitySceneDefinition Import(string json) => Normalize(Deserialize(json));

    public string Export(CitySceneDefinition scene)
        => JsonSerializer.Serialize(Normalize(scene), AppJsonSerializerContext.Default.CitySceneDefinition);

    private static CitySceneDefinition Deserialize(string json)
        => JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.CitySceneDefinition)
           ?? throw new InvalidOperationException("Файл сцены пуст.");

    private static CitySceneDefinition Normalize(CitySceneDefinition scene)
    {
        scene.CameraSize = Math.Clamp(scene.CameraSize, 6, 20);
        scene.GroundColor = Color(scene.GroundColor, "#07110B");
        scene.GridColor = Color(scene.GridColor, "#294A32");
        scene.Core ??= new();
        scene.Core.Width = Math.Clamp(scene.Core.Width, 3, 9);
        scene.Core.Depth = Math.Clamp(scene.Core.Depth, 2.4, 7);
        scene.Core.Color = Color(scene.Core.Color, "#245235");
        scene.Chips ??= [];
        scene.Routes ??= [];
        foreach (var chip in scene.Chips)
        {
            chip.Width = Math.Clamp(chip.Width, 1.8, 6);
            chip.Depth = Math.Clamp(chip.Depth, 1.2, 4.5);
            chip.Color = Color(chip.Color, "#21492F");
        }
        foreach (var route in scene.Routes)
        {
            route.Color = Color(route.Color, "#B7ED63");
            route.Points ??= [];
        }
        return scene;
    }

    private static string Color(string value, string fallback)
        => !string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#' ? value : fallback;
}
