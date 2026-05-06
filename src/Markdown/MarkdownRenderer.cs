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
        // Stub: emit raw text. Parser comes in Task 2+.
        yield return new Run(source ?? string.Empty);
    }
}
