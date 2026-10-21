using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// Minimal, dependency-free markdown rendering for the one place this app ever produces
/// markdown: the TaskBrief builder (headings, bold, bullet lists) and the AI description
/// prompt. Deliberately not a general-purpose parser - just enough to never show a user raw
/// "##"/"**" syntax, without pulling in an experimental third-party control.
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>Renders markdown into a RichTextBlock for read-only previews.</summary>
    public static void RenderInto(RichTextBlock target, string? markdown)
    {
        target.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue; // paragraphs already carry their own spacing below
            }

            var paragraph = new Paragraph();

            if (TryGetHeading(line, out var level, out var headingText))
            {
                paragraph.FontSize = level switch { 1 => 20, 2 => 17, _ => 15 };
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Margin = new Thickness(0, level == 1 ? 10 : 8, 0, 2);
                AppendInlines(paragraph.Inlines, headingText);
            }
            else if (TryGetBullet(line, out var bulletText))
            {
                paragraph.Margin = new Thickness(12, 0, 0, 2);
                paragraph.Inlines.Add(new Run { Text = "• " });
                AppendInlines(paragraph.Inlines, bulletText);
            }
            else
            {
                paragraph.Margin = new Thickness(0, 0, 0, 4);
                AppendInlines(paragraph.Inlines, line);
            }

            target.Blocks.Add(paragraph);
        }
    }

    /// <summary>Flattens markdown into a short, syntax-free one-line-ish preview - for compact
    /// card views where a full RichTextBlock would be overkill.</summary>
    public static string StripSyntax(string? markdown, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryGetHeading(line, out _, out var headingText))
            {
                line = headingText;
            }
            else if (TryGetBullet(line, out var bulletText))
            {
                line = bulletText;
            }

            line = line.Replace("**", string.Empty);

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(line);

            if (sb.Length >= maxLength)
            {
                break;
            }
        }

        var result = sb.ToString();
        return result.Length > maxLength ? result[..maxLength] + "..." : result;
    }

    private static bool TryGetHeading(string line, out int level, out string text)
    {
        var i = 0;
        while (i < line.Length && line[i] == '#')
        {
            i++;
        }

        if (i == 0 || i > 6 || i >= line.Length || line[i] != ' ')
        {
            level = 0;
            text = line;
            return false;
        }

        level = i;
        text = line[(i + 1)..].Trim();
        return true;
    }

    private static bool TryGetBullet(string line, out string text)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            text = line[2..];
            return true;
        }

        text = line;
        return false;
    }

    /// <summary>Only **bold** - the one inline style this app's generated markdown ever uses.</summary>
    private static void AppendInlines(InlineCollection inlines, string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var boldStart = text.IndexOf("**", i, StringComparison.Ordinal);
            if (boldStart < 0)
            {
                inlines.Add(new Run { Text = text[i..] });
                return;
            }

            if (boldStart > i)
            {
                inlines.Add(new Run { Text = text[i..boldStart] });
            }

            var boldEnd = text.IndexOf("**", boldStart + 2, StringComparison.Ordinal);
            if (boldEnd < 0)
            {
                inlines.Add(new Run { Text = text[boldStart..] });
                return;
            }

            var bold = new Bold();
            bold.Inlines.Add(new Run { Text = text[(boldStart + 2)..boldEnd] });
            inlines.Add(bold);

            i = boldEnd + 2;
        }
    }
}
