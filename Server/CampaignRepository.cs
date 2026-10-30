using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synapse.Blocks.Graph;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Server;

public sealed class CampaignConflictException : Exception;
public sealed record CampaignSnapshot(byte[] Json, string ETag);

// One server process owns the campaign; replace-on-save keeps the last valid file intact.
public sealed class CampaignRepository(IWebHostEnvironment environment, IConfiguration configuration, CampaignMediaStore media)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = Path.GetFullPath(configuration["Storage:Directory"] ??
        Path.Combine(environment.ContentRootPath, "App_Data"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private string FilePath => Path.Combine(_directory, "levels.json");
    private CampaignSnapshot? _cached;
    private (long WriteTime, long Length) _stamp;
    private static readonly JsonSerializerOptions WireOptions = new(JsonOptions) { WriteIndented = false };

    public async Task<CampaignSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<CampaignSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        if (!File.Exists(FilePath))
        {
            var seed = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "Data", "seed-levels.json"), cancellationToken);
            await WriteAtomicAsync(Encoding.UTF8.GetBytes(seed), cancellationToken);
        }
        var info = new FileInfo(FilePath);
        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
        if (_cached is not null && stamp == _stamp) return _cached;
        var json = await File.ReadAllBytesAsync(FilePath, cancellationToken);
        var levels = JsonSerializer.Deserialize<List<LevelDefinition>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Не удалось прочитать кампанию.");
        Validate(levels);
        await media.ExternalizeAsync(levels, cancellationToken);
        _cached = new(JsonSerializer.SerializeToUtf8Bytes(levels, WireOptions), ETag(json));
        _stamp = stamp;
        return _cached;
    }

    public async Task<string> SaveAsync(List<LevelDefinition> levels, string expectedETag, CancellationToken cancellationToken = default)
    {
        Validate(levels);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadCoreAsync(cancellationToken);
            if (string.IsNullOrEmpty(expectedETag) || expectedETag != current.ETag) throw new CampaignConflictException();
            await media.ExternalizeAsync(levels, cancellationToken);
            var json = JsonSerializer.SerializeToUtf8Bytes(levels.OrderBy(level => level.Order).ToList(), JsonOptions);
            await WriteAtomicAsync(json, cancellationToken);
            _cached = null;
            return ETag(json);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAtomicAsync(byte[] json, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, json, cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ETag(byte[] json) => $"\"{Convert.ToHexString(SHA256.HashData(json))}\"";

    private static void Validate(List<LevelDefinition> levels)
    {
        if (levels is null || levels.Count is < 1 or > 500)
            throw new ArgumentException("Кампания должна содержать от 1 до 500 уровней.");
        var ids = new HashSet<Guid>();
        foreach (var level in levels)
        {
            if (level is null || level.Id == Guid.Empty || !ids.Add(level.Id))
                throw new ArgumentException("Уровням нужны уникальные непустые идентификаторы.");
            if (string.IsNullOrWhiteSpace(level.Title) || level.Title.Length > 200)
                throw new ArgumentException("У каждого уровня должно быть название до 200 символов.");
            if (level.Tests is null || level.Tests.Count is < 1 or > 200 || level.Tests.Any(test =>
                    test is null || test.Input is null || test.ExpectedOutput is null || test.Name is null))
                throw new ArgumentException("У каждого уровня должно быть от 1 до 200 заполненных проверок.");
            if (level.Tests.Any(test => test.Id == Guid.Empty) || level.Tests.Select(test => test.Id).Distinct().Count() != level.Tests.Count)
                throw new ArgumentException("Проверкам нужны уникальные непустые идентификаторы.");
            if (level.AllowedNodes is null || level.AllowedNodes.Count > 200 ||
                level.AllowedNodes.Any(target => target is null || !LevelNodes.IsKnownTarget(target, NodeRegistry.Default)) ||
                LevelNodes.Mandatory.Any(typeId => !level.AllowedNodes.Contains(typeId, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Проверьте доступные блоки: все должны быть известны, «Вход» и «Выход» обязательны.");
            if (level.Rules is null || level.Rules.Count > 50 || level.Rules.Any(rule => rule is null ||
                    !Enum.IsDefined(rule.Kind) || rule.Count is < 0 or > 99 || rule.Target is null ||
                    !LevelNodes.IsKnownTarget(rule.Target, NodeRegistry.Default)))
                throw new ArgumentException("Проверьте условия решения: блок или раздел должен существовать, количество — от 0 до 99.");
            if (level.Chapter < 1)
                throw new ArgumentException("Номер главы должен быть положительным.");
            if (level.IntroSteps is null || level.IntroSteps.Count > 100 || level.IntroSteps.Any(step => step is null ||
                    step.Title is null || step.Body is null || step.FieldValue is null || step.MediaUrl is null ||
                    !Enum.IsDefined(step.FieldKind) || !Enum.IsDefined(step.HighlightTarget)))
                throw new ArgumentException("Проверьте вступительные окна (не более 100 на уровень).");
            if (new[] { level.Direction, level.Location, level.Description, level.StoryIntro, level.StorySuccess,
                level.Objective, level.InputDescription, level.OutputDescription, level.Hint }.Any(value => value is null))
                throw new ArgumentException("Текстовые поля уровня не должны быть null.");
        }
        if (levels.Select(level => level.Order).Distinct().Count() != levels.Count || levels.Any(level => level.Order < 1))
            throw new ArgumentException("Порядок уровней должен содержать уникальные положительные числа.");
    }
}
