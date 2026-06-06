using Markdig;
using Microsoft.UI.Xaml;

namespace TranscribeApp;

/// <summary>Converts markdown to a themed HTML document for display in a WebView2.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static string ToHtml(string? markdown, ElementTheme theme)
    {
        var body = Markdown.ToHtml(markdown ?? "", Pipeline);
        var dark = theme == ElementTheme.Dark;
        var fg = dark ? "#E6E6E6" : "#1A1A1A";
        var muted = dark ? "#9AA0A6" : "#5A5A5A";
        var border = dark ? "#3A3A3A" : "#D6D6D6";
        var accent = dark ? "#7EC8FF" : "#0A6CCC";
        var subtle = dark ? "rgba(255,255,255,.06)" : "rgba(0,0,0,.05)";
        var css = $@"
<style>
  html,body{{margin:0;padding:0;background:transparent;color:{fg};
    font-family:'Segoe UI Variable Text','Segoe UI',sans-serif;font-size:14px;line-height:1.5;
    word-wrap:break-word;overflow-x:hidden;}}
  h1,h2,h3,h4{{margin:.7em 0 .3em;font-weight:600;line-height:1.25;}}
  h1{{font-size:1.3em;}} h2{{font-size:1.15em;}} h3{{font-size:1.05em;}}
  p{{margin:.4em 0;}}
  a{{color:{accent};}}
  code{{background:{subtle};padding:.1em .35em;border-radius:4px;font-size:.92em;}}
  pre{{background:{subtle};padding:.6em .8em;border-radius:6px;overflow:auto;}}
  pre code{{background:transparent;padding:0;}}
  ul,ol{{margin:.3em 0 .3em 1.3em;padding:0;}}
  li{{margin:.15em 0;}}
  table{{border-collapse:collapse;margin:.5em 0;width:auto;}}
  th,td{{border:1px solid {border};padding:.35em .6em;text-align:left;}}
  th{{background:{subtle};}}
  blockquote{{margin:.4em 0;padding:.2em .9em;border-left:3px solid {border};color:{muted};}}
  hr{{border:none;border-top:1px solid {border};margin:.8em 0;}}
</style>";
        return $"<!doctype html><html><head><meta charset='utf-8'>{css}</head><body>{body}</body></html>";
    }
}
