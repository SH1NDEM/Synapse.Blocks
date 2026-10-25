using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Server;

public sealed class CampaignMediaStore(IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(configuration["Storage:Directory"] ??
        Path.Combine(environment.ContentRootPath, "App_Data")), "media");
    private static readonly Regex FileNamePattern = new("^[a-f0-9]{64}\\.(gif|png|jpg|webp)$", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ["image/gif"] = "gif", ["image/png"] = "png", ["image/jpeg"] = "jpg", ["image/webp"] = "webp" };

    public async Task ExternalizeAsync(IEnumerable<LevelDefinition> levels, CancellationToken cancellationToken)
    {
        foreach (var step in levels.SelectMany(level => level.IntroSteps))
        {
            if (!step.MediaUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = step.MediaUrl.IndexOf(',');
            if (separator < 0) throw new ArgumentException("Некорректное встроенное изображение.");
            var header = step.MediaUrl[5..separator];
            var mime = header.Split(';')[0];
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) || !Extensions.TryGetValue(mime, out var extension))
                throw new ArgumentException("Поддерживаются встроенные GIF, PNG, JPEG и WebP.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(step.MediaUrl[(separator + 1)..]); }
            catch (FormatException) { throw new ArgumentException("Некорректные данные изображения."); }
            if (bytes.Length > 4 * 1024 * 1024) throw new ArgumentException("Изображение не должно превышать 4 МБ.");
            Directory.CreateDirectory(_directory);
            var name = $"{Convert.ToHexStringLower(SHA256.HashData(bytes))}.{extension}";
            var path = Path.Combine(_directory, name);
            if (!File.Exists(path))
            {
                var temporary = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            step.MediaUrl = $"/api/media/{name}";
        }
    }

    public (string Path, string Mime)? Find(string name)
    {
        if (!FileNamePattern.IsMatch(name)) return null;
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) return null;
        var extension = Path.GetExtension(name)[1..];
        var mime = Extensions.First(pair => pair.Value == extension).Key;
        return (path, mime);
    }
}
