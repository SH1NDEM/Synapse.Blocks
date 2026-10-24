using Synapse.Blocks.Text;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}");
}

var formatted = LevelMarkdown.Render("## Задание\n\nСоедините **два блока** и передайте `6`.\n\n- Вход\n- Выход\n\n```csharp\nreturn 6;\n```\n\n| Вход | Ответ |\n| --- | --- |\n| 6 | 6 |");
Check(formatted.Contains("<h2>") && formatted.Contains("<strong>") && formatted.Contains("<ul>") && formatted.Contains("<pre><code") && formatted.Contains("<table>"), "Markdown headings, emphasis, lists, code and tables");
var literal = LevelMarkdown.Render("<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>");
Check(!literal.Contains("<script>", StringComparison.OrdinalIgnoreCase) && !literal.Contains("<img", StringComparison.OrdinalIgnoreCase) && literal.Contains("&lt;"), "Raw HTML is rendered as text");
foreach (var url in new[] { "javascript:alert(1)", "jav&#x61;script:alert(1)", "data:text/html,test", "vbscript:msgbox(1)" })
{
    var unsafeLink = LevelMarkdown.Render($"[link]({url})");
    Check(!unsafeLink.Contains("href=\"javascript:", StringComparison.OrdinalIgnoreCase) && !unsafeLink.Contains("href=\"data:", StringComparison.OrdinalIgnoreCase) && !unsafeLink.Contains("href=\"vbscript:", StringComparison.OrdinalIgnoreCase), $"Unsafe link blocked: {url}");
}
var safe = LevelMarkdown.Render("[docs](https://example.com/docs)\n\n![example](gifs/connect-nodes.gif)");
Check(safe.Contains("href=\"https://example.com/docs\"") && safe.Contains("src=\"gifs/connect-nodes.gif\""), "HTTPS links and relative images work");
Check(LevelMarkdown.Render(null) == "", "Empty descriptions render without errors");
