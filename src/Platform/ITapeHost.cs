using System;
using Avalonia;

namespace Stickies.Platform;

// One per locked note. Show/Hide toggles visibility on lock state change;
// Update is called whenever the note moves, resizes, or activates so the
// tape stays glued to the top of the note's silhouette.
//
// Implementations live in platform-specific assemblies behind the same wall
// as IHotkeyHost: Stickies.Win32.WinTapeHost, Stickies.Platform.AvaloniaTapeHost
// (Mac/Linux), and a NullTapeHost fallback when init fails.
internal interface ITapeHost : IDisposable
{
    void Show();
    void Hide();
    // noteBounds is in physical (device) pixels — same coordinate space as
    // Window.Position. scale is the owner's DesktopScaling so implementations
    // can convert TapeInset/TapeHeight/Pad (defined in DIPs) into physical
    // pixels for the bitmap render and for positioning math.
    void Update(PixelRect noteBounds, double scale);
}
