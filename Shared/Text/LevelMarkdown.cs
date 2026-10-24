using System.Net;
using Markdig;
using Markdig.Renderers;

namespace Synapse.Blocks.Text;

public static class LevelMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().DisableHtml().Build();

    public static string Render(string? markdown)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { LinkRewriter = SafeUrl };
        Pipeline.Setup(renderer);
        renderer.Render(Markdown.Parse(markdown ?? "", Pipeline));
        return writer.ToString();
    }

    private static string SafeUrl(string url)
    {
        var decoded = WebUtility.HtmlDecode(url);
        var clean = string.Concat(decoded.Where(character => !char.IsControl(character))).Trim();
        if (!Uri.TryCreate(clean, UriKind.RelativeOrAbsolute, out var uri)) return "#";
        return !uri.IsAbsoluteUri || uri.Scheme is "http" or "https" or "mailto" ? clean : "#";
    }
}
