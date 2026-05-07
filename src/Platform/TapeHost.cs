using System;
using Avalonia.Controls;

namespace Stickies.Platform;

// Static factory: returns the right ITapeHost for the running OS.
//
// Windows can't use Avalonia's transparent-window path because we ship with
// Win32RenderingMode.Software (see Program.cs) — RedirectionSurface has no
// per-pixel alpha and undrawn pixels render as opaque black. Win32 layered
// windows bypass Avalonia's render pipeline entirely and composite an ARGB
// bitmap via DWM, so we get real transparency without ANGLE.
//
// Mac and Linux default to GPU rendering (Metal/Egl), so Avalonia's
// TransparencyLevelHint=Transparent works as advertised — share a single
// AvaloniaTapeHost between them.
internal static class TapeHost
{
    public static ITapeHost Create(Window owner)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero) return new NullTapeHost();
                return new Stickies.Win32.WinTapeHost(handle);
            }
            return new AvaloniaTapeHost(owner);
        }
        catch
        {
            // Init can fail on unusual platform configs (Wayland-only Linux,
            // sandboxed Mac without windowing entitlements, etc.). The lock
            // feature still works — just no visible tape.
        }
        return new NullTapeHost();
    }

    private sealed class NullTapeHost : ITapeHost
    {
        public void Show() { }
        public void Hide() { }
        public void Update(Avalonia.PixelRect noteBounds, double scale) { }
        public void Dispose() { }
    }
}
