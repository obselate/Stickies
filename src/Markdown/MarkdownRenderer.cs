using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
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
                foreach (var inl in WrapHeading(line.Substring(4), 15, bodyColor)) yield return inl;
            }
            else if (line.StartsWith("## ") && line.Length > 3)
            {
                foreach (var inl in WrapHeading(line.Substring(3), 17, bodyColor)) yield return inl;
            }
            else if (line.StartsWith("# ") && line.Length > 2)
            {
                foreach (var inl in WrapHeading(line.Substring(2), 20, bodyColor)) yield return inl;
            }
            // Task list — checkbox stub (real CheckBox in Task 6).
            else if (line.StartsWith("- [ ] ") && line.Length > 6)
            {
                yield return new Run("☐  ");
                foreach (var inl in ScanInlines(line.Substring(6), bodyColor)) yield return inl;
            }
            else if (line.StartsWith("- [x] ") && line.Length > 6)
            {
                yield return new Run("☑  ");
                foreach (var inl in ScanInlines(line.Substring(6), bodyColor)) yield return inl;
            }
            // Regular bullet.
            else if (line.StartsWith("- ") && line.Length > 2)
            {
                yield return new Run("•  ");
                foreach (var inl in ScanInlines(line.Substring(2), bodyColor)) yield return inl;
            }
            else
            {
                foreach (var inl in ScanInlines(line, bodyColor)) yield return inl;
            }

            if (i < lines.Length - 1)
                yield return new LineBreak();
        }
    }

    private static IEnumerable<Inline> WrapHeading(string text, double fontSize, Color bodyColor)
    {
        var span = new Span { FontSize = fontSize, FontWeight = FontWeight.Bold };
        foreach (var inl in ScanInlines(text, bodyColor)) span.Inlines.Add(inl);
        yield return span;
    }

    // Scans inline markers in a single line of text and emits Inlines.
    // Recognises `code`, **bold**, *italic*, and **bold *with italic***.
    // Unclosed markers render as literal text.
    private static IEnumerable<Inline> ScanInlines(string text, Color bodyColor)
    {
        int i = 0;
        int n = text.Length;
        var literal = new StringBuilder();
        var top = new List<Inline>();

        while (i < n)
        {
            // Inline code (highest precedence — leaf only, no nesting).
            if (text[i] == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close >= 0)
                {
                    FlushLiteral(top, literal);
                    var inner = text.Substring(i + 1, close - (i + 1));
                    top.Add(MakeInlineCode(inner, bodyColor));
                    i = close + 1;
                    continue;
                }
            }

            // ** (bold) — must check before * to avoid eating the first asterisk.
            if (i + 1 < n && text[i] == '*' && text[i + 1] == '*')
            {
                int close = FindClosingDouble(text, i + 2);
                if (close >= 0)
                {
                    FlushLiteral(top, literal);
                    var inner = text.Substring(i + 2, close - (i + 2));
                    var bold = new Bold();
                    foreach (var inl in ScanInlinesNoBold(inner, bodyColor)) bold.Inlines.Add(inl);
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
    private static IEnumerable<Inline> ScanInlinesNoBold(string text, Color bodyColor)
    {
        int i = 0;
        int n = text.Length;
        var literal = new StringBuilder();
        var sink = new List<Inline>();

        while (i < n)
        {
            if (text[i] == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close >= 0)
                {
                    FlushLiteral(sink, literal);
                    var inner = text.Substring(i + 1, close - (i + 1));
                    sink.Add(MakeInlineCode(inner, bodyColor));
                    i = close + 1;
                    continue;
                }
            }

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

    private static InlineUIContainer MakeInlineCode(string text, Color bodyColor)
    {
        var bg = DarkenInHsl(bodyColor, 12);
        var border = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                FontSize = 13,
            }
        };
        return new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.Center };
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
                if (j + 1 < text.Length && text[j + 1] == '*') continue;
                return j;
            }
        }
        return -1;
    }

    private static Color DarkenInHsl(Color rgb, double percent)
    {
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9)
        {
            h = 0; s = 0;
        }
        else
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d) + (g < b ? 6 : 0);
            else if (max == g) h = ((b - r) / d) + 2;
            else h = ((r - g) / d) + 4;
            h /= 6;
        }

        l = Math.Max(0, Math.Min(1, l - percent / 100.0));

        double r2, g2, b2;
        if (Math.Abs(s) < 1e-9)
        {
            r2 = g2 = b2 = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r2 = HueToRgb(p, q, h + 1.0 / 3.0);
            g2 = HueToRgb(p, q, h);
            b2 = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        return Color.FromArgb(rgb.A, (byte)(r2 * 255), (byte)(g2 * 255), (byte)(b2 * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
