using System.Text.Json;
using Microsoft.JSInterop;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class CitySceneStore(HttpClient http, IJSRuntime js)
{
    private const string StorageKey = "synapse-city-scene-v1";
    private const string BaseChipColor = "#21492F";
    private const string ExtraChipColor = "#2F65A7";
    private const string BaseRouteColor = "#B7ED63";
    private const string ExtraRouteColor = "#6CA8FF";

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

    /// <summary>
    /// Синхронизирует системную плату с кампанией: каждому уровню соответствует
    /// один пронумерованный чип. Уже настроенные чипы и маршруты сохраняются.
    /// </summary>
    public CitySceneDefinition MatchLevelCount(CitySceneDefinition scene, int levelCount)
    {
        scene = Normalize(scene);
        levelCount = Math.Max(1, levelCount);

        var chips = new List<CityChipDefinition>(levelCount);
        for (var number = 1; number <= levelCount; number++)
        {
            var chip = scene.Chips.FirstOrDefault(item => item.Number == number);
            if (chip is null)
            {
                var position = DefaultChipPosition(number);
                chip = new CityChipDefinition
                {
                    Number = number,
                    X = position.X,
                    Z = position.Z,
                    Color = number >= 7 ? ExtraChipColor : BaseChipColor
                };
            }
            else if (number >= 7 && string.Equals(chip.Color, BaseChipColor, StringComparison.OrdinalIgnoreCase))
            {
                // Автоматически созданные ранее зелёные чипы новых задач переводим
                // в синюю группу, не затирая пользовательские цвета редактора.
                chip.Color = ExtraChipColor;
            }
            chips.Add(chip);
        }
        scene.Chips = chips;

        var routes = new List<CityRouteDefinition>(levelCount);
        foreach (var chip in chips)
        {
            var route = scene.Routes.FirstOrDefault(item => item.ChipNumber == chip.Number);
            if (route is null)
            {
                route = new CityRouteDefinition
                {
                    ChipNumber = chip.Number,
                    Color = chip.Number >= 7 ? ExtraRouteColor : BaseRouteColor,
                    Points =
                    [
                        new() { X = chip.X, Z = chip.Z },
                        new() { X = chip.X * .48, Z = chip.Z * .48 },
                        new() { X = scene.Core.X, Z = scene.Core.Z }
                    ]
                };
            }
            else if (chip.Number >= 7 && string.Equals(route.Color, BaseRouteColor, StringComparison.OrdinalIgnoreCase))
            {
                route.Color = ExtraRouteColor;
            }
            routes.Add(route);
        }
        scene.Routes = routes;
        return Normalize(scene);
    }

    private static CitySceneDefinition Deserialize(string json)
        => JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.CitySceneDefinition)
           ?? throw new InvalidOperationException("Файл сцены пуст.");

    private static (double X, double Z) DefaultChipPosition(int number)
    {
        // Первые позиции образуют асимметричную композицию вокруг центрального чипа.
        // После восьмого чипа новые элементы расходятся по следующим внешним кольцам.
        return number switch
        {
            1 => (-7.8, -5.15),
            2 => (7.1, -6.6),
            3 => (-8, 8.9),
            4 => (10.4, 5.25),
            5 => (-12, 1.2),
            6 => (12, -1.1),
            7 => (-3.8, -10.8),
            8 => (4.4, 10.9),
            9 => (-12.5, -8.8),
            10 => (12.7, 8.7),
            _ => RingPosition(number)
        };
    }

    private static (double X, double Z) RingPosition(int number)
    {
        var index = number - 11;
        var ring = index / 8;
        var slot = index % 8;
        var radius = 14d + ring * 4d;
        var angle = -Math.PI / 2 + slot * Math.PI / 4;
        return (Math.Round(Math.Cos(angle) * radius, 2), Math.Round(Math.Sin(angle) * radius, 2));
    }

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
            chip.Color = Color(chip.Color, chip.Number >= 7 ? ExtraChipColor : BaseChipColor);
        }
        foreach (var route in scene.Routes)
        {
            route.Color = Color(route.Color, route.ChipNumber >= 7 ? ExtraRouteColor : BaseRouteColor);
            route.Points ??= [];
        }
        return scene;
    }

    private static string Color(string value, string fallback)
        => !string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#' ? value : fallback;
}
