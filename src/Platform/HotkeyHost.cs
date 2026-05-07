using System;

namespace Stickies.Platform;

// Static factory: returns the right IHotkeyHost for the running OS.
// On unsupported platforms (or when platform init fails — e.g. Linux on
// pure Wayland), returns NullHotkeyHost so the rest of the app stays
// functional with global-hotkey silently no-op'd.
//
// Mac and Linux branches land in the c0r and 49f beads.
internal static class HotkeyHost
{
    public static IHotkeyHost Create()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var w = new Stickies.Win32.HotkeyHost();
                w.Show();
                return w;
            }
            if (OperatingSystem.IsMacOS())
            {
                return new Stickies.Mac.HotkeyHost();
            }
        }
        catch
        {
            // Platform-specific init can fail; fall through to no-op.
        }
        return new NullHotkeyHost();
    }

    private sealed class NullHotkeyHost : IHotkeyHost
    {
#pragma warning disable CS0067
        public event Action? HotkeyPressed;
#pragma warning restore CS0067
        public void Register(HotkeyModifier mods, uint vk) { }
        public void Dispose() { }
    }
}
