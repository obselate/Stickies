using System;
using System.Collections.Generic;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Stickies;

internal static class MarkdownRenderer
{
    public static IEnumerable<Inline> Render(
        string source,
        Color bodyColor,
        Action<int, bool> onCheckboxToggle,
        Action<string> onLinkClicked)
    {
        if (string.IsNullOrEmpty(source)) yield break;

        var lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                if (i < lines.Length - 1) yield return new LineBreak();
                continue;
            }

            // Headings (most specific first to avoid # matching ###).
            if (line.StartsWith("### ") && line.Length > 4)
            {
                yield return MakeHeading(line.Substring(4), fontSize: 15);
            }
            else if (line.StartsWith("## ") && line.Length > 3)
            {
                yield return MakeHeading(line.Substring(3), fontSize: 17);
            }
            else if (line.StartsWith("# ") && line.Length > 2)
            {
                yield return MakeHeading(line.Substring(2), fontSize: 20);
            }
            // Task list — checkbox stub (real CheckBox in Task 6).
            else if (line.StartsWith("- [ ] ") && line.Length > 6)
            {
                yield return new Run("☐  " + line.Substring(6));
            }
            else if (line.StartsWith("- [x] ") && line.Length > 6)
            {
                yield return new Run("☑  " + line.Substring(6));
            }
            // Regular bullet.
            else if (line.StartsWith("- ") && line.Length > 2)
            {
                yield return new Run("•  " + line.Substring(2));
            }
            else
            {
                yield return new Run(line);
            }

            if (i < lines.Length - 1)
                yield return new LineBreak();
        }
    }

    private static Span MakeHeading(string text, double fontSize)
    {
        var span = new Span { FontSize = fontSize, FontWeight = FontWeight.Bold };
        span.Inlines.Add(new Run(text));
        return span;
    }
}
