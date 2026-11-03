using System.Net;
using Markdig;

namespace ClipboardCollector;

/// <summary>
/// Turns the collected Markdown into a standalone HTML page for the preview and the PDF export.
/// </summary>
internal static class MarkdownHtml
{
    // Raw HTML is shown as text: clipped content must never run scripts or load hidden content.
    // Single line breaks are kept, since text copied from web pages often relies on them.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    private const string BaseCss = """
        :root { color-scheme: light; --fg: #1b1b1b; --muted: #5f5f5f; --rule: #e0e0e0; --code-bg: #f4f4f4; --link: #005fb8; }
        html { font: 15px/1.6 "Segoe UI Variable Text", "Segoe UI", sans-serif; color: var(--fg); }
        body { max-width: 52rem; margin: 0 auto; padding: 1.25rem 1.5rem 3rem; overflow-wrap: anywhere; }
        h1, h2, h3, h4 { line-height: 1.25; margin: 1.6em 0 0.5em; font-weight: 600; }
        h1 { font-size: 1.7em; } h2 { font-size: 1.35em; } h3 { font-size: 1.15em; }
        body > :first-child { margin-top: 0; }
        a { color: var(--link); }
        hr { border: 0; border-top: 1px solid var(--rule); margin: 2em 0; }
        code { font: 0.9em/1.5 "Cascadia Mono", Consolas, monospace; background: var(--code-bg); padding: 0.1em 0.3em; border-radius: 4px; }
        pre { background: var(--code-bg); padding: 0.8em 1em; border-radius: 6px; white-space: pre-wrap; }
        pre code { background: none; padding: 0; }
        blockquote { margin: 1em 0; padding: 0 1em; color: var(--muted); border-left: 3px solid var(--rule); }
        table { border-collapse: collapse; margin: 1em 0; }
        th, td { border: 1px solid var(--rule); padding: 0.35em 0.7em; text-align: left; vertical-align: top; }
        th { background: var(--code-bg); }
        img { max-width: 100%; }
        """;

    // The preview follows the Windows theme; the PDF is always light
    private const string ScreenCss = """
        @media (prefers-color-scheme: dark) {
          :root { color-scheme: dark; --fg: #e6e6e6; --muted: #a8a8a8; --rule: #3a3a3a; --code-bg: #2a2a2a; --link: #8cc8ff; }
          html { background: #1f1f1f; }
        }
        """;

    private const string PrintCss = """
        body { max-width: none; padding: 0; }
        pre, blockquote, table, img { break-inside: avoid; }
        h1, h2, h3, h4 { break-after: avoid; }
        """;

    public static string ToHtml(string markdown, string title, bool forPrint)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        var css = BaseCss + (forPrint ? PrintCss : ScreenCss);
        return $"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(title)}</title>
            <style>{css}</style></head>
            <body>{body}</body></html>
            """;
    }
}
