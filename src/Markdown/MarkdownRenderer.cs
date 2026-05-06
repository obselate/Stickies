using System;
using System.Collections.Generic;
using System.Text;
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
                foreach (var inl in WrapHeading(line.Substring(4), fontSize: 15)) yield return inl;
            }
            else if (line.StartsWith("## ") && line.Length > 3)
            {
                foreach (var inl in WrapHeading(line.Substring(3), fontSize: 17)) yield return inl;
            }
            else if (line.StartsWith("# ") && line.Length > 2)
            {
                foreach (var inl in WrapHeading(line.Substring(2), fontSize: 20)) yield return inl;
            }
            // Task list — checkbox stub (real CheckBox in Task 6).
            else if (line.StartsWith("- [ ] ") && line.Length > 6)
            {
                yield return new Run("☐  ");
                foreach (var inl in ScanInlines(line.Substring(6))) yield return inl;
            }
            else if (line.StartsWith("- [x] ") && line.Length > 6)
            {
                yield return new Run("☑  ");
                foreach (var inl in ScanInlines(line.Substring(6))) yield return inl;
            }
            // Regular bullet.
            else if (line.StartsWith("- ") && line.Length > 2)
            {
                yield return new Run("•  ");
                foreach (var inl in ScanInlines(line.Substring(2))) yield return inl;
            }
            else
            {
                foreach (var inl in ScanInlines(line)) yield return inl;
            }

            if (i < lines.Length - 1)
                yield return new LineBreak();
        }
    }

    private static IEnumerable<Inline> WrapHeading(string text, double fontSize)
    {
        var span = new Span { FontSize = fontSize, FontWeight = FontWeight.Bold };
        foreach (var inl in ScanInlines(text)) span.Inlines.Add(inl);
        yield return span;
    }

    // Scans inline markers in a single line of text and emits Inlines.
    // Recognises **bold**, *italic*, and **bold *with italic***.
    // Unclosed markers render as literal text.
    private static IEnumerable<Inline> ScanInlines(string text)
    {
        int i = 0;
        int n = text.Length;
        var literal = new StringBuilder();
        var top = new List<Inline>();

        while (i < n)
        {
            // Look for ** (bold) — must check before * to avoid eating the first asterisk.
            if (i + 1 < n && text[i] == '*' && text[i + 1] == '*')
            {
                int close = FindClosingDouble(text, i + 2);
                if (close >= 0)
                {
                    FlushLiteral(top, literal);
                    var inner = text.Substring(i + 2, close - (i + 2));
                    var bold = new Bold();
                    foreach (var inl in ScanInlinesNoBold(inner)) bold.Inlines.Add(inl);
                    top.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            if (text[i] == '*')
            {
                int close = FindClosingSingle(text, i + 1);
                if (close >= 0)
                {
                    FlushLiteral(top, literal);
                    var inner = text.Substring(i + 1, close - (i + 1));
                    var italic = new Italic();
                    italic.Inlines.Add(new Run(inner));
                    top.Add(italic);
                    i = close + 1;
                    continue;
                }
            }

            literal.Append(text[i]);
            i++;
        }
        FlushLiteral(top, literal);
        return top;
    }

    // Same as ScanInlines but does not recurse into bold (we're already inside one).
    private static IEnumerable<Inline> ScanInlinesNoBold(string text)
    {
        int i = 0;
        int n = text.Length;
        var literal = new StringBuilder();
        var sink = new List<Inline>();

        while (i < n)
        {
            if (text[i] == '*' && (i + 1 >= n || text[i + 1] != '*'))
            {
                int close = FindClosingSingle(text, i + 1);
                if (close >= 0)
                {
                    FlushLiteral(sink, literal);
                    var inner = text.Substring(i + 1, close - (i + 1));
                    var italic = new Italic();
                    italic.Inlines.Add(new Run(inner));
                    sink.Add(italic);
                    i = close + 1;
                    continue;
                }
            }
            literal.Append(text[i]);
            i++;
        }
        FlushLiteral(sink, literal);
        return sink;
    }

    private static void FlushLiteral(List<Inline> sink, StringBuilder literal)
    {
        if (literal.Length > 0)
        {
            sink.Add(new Run(literal.ToString()));
            literal.Clear();
        }
    }

    private static int FindClosingDouble(string text, int from)
    {
        for (int j = from; j + 1 < text.Length; j++)
            if (text[j] == '*' && text[j + 1] == '*') return j;
        return -1;
    }

    private static int FindClosingSingle(string text, int from)
    {
        for (int j = from; j < text.Length; j++)
        {
            if (text[j] == '*')
            {
                // Single * must not be followed by another * (that's bold's territory).
                if (j + 1 < text.Length && text[j + 1] == '*') continue;
                return j;
            }
        }
        return -1;
    }
}
