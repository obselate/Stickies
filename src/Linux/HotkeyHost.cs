using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Threading;
using Stickies.Platform;

namespace Stickies.Linux;

// Pattern A platform-specific: only constructed by Stickies.Platform.HotkeyHost.Create()
// when OperatingSystem.IsLinux(). Uses the X11 grab/event API; on pure Wayland (no
// XWayland) XOpenDisplay returns null and the constructor throws — caught by the factory's
// try/catch which yields NullHotkeyHost. README documents the Wayland limitation.
//
// We grab the key 4× to cover NumLock/CapsLock modifier-mask permutations so the hotkey
// works regardless of those toggle states.
[SupportedOSPlatform("linux")]
internal sealed unsafe partial class HotkeyHost : IHotkeyHost
{
    private const string Lib = "libX11.so.6";

    private const uint ShiftMask   = 1;
    private const uint LockMask    = 2;  // CapsLock
    private const uint ControlMask = 4;
    private const uint Mod1Mask    = 8;  // Alt
    private const uint Mod2Mask    = 16; // NumLock
    private const uint Mod4Mask    = 64; // Super/Win

    private const int KeyPress = 2;

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(80)] public uint state;
        [FieldOffset(84)] public uint keycode;
    }

    private readonly IntPtr _display;
    private readonly IntPtr _root;
    private uint _modifiers;
    private byte _keycode;
    private Thread? _eventThread;
    private volatile bool _running;

    public event Action? HotkeyPressed;

    public HotkeyHost()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("XOpenDisplay returned null (no X11 display — likely pure Wayland)");
        _root = XDefaultRootWindow(_display);
    }

    public void Register(HotkeyModifier mods, uint vk)
    {
        if (_keycode != 0) return;

        uint x11Mods = 0;
        if ((mods & HotkeyModifier.Control) != 0) x11Mods |= ControlMask;
        if ((mods & HotkeyModifier.Shift)   != 0) x11Mods |= ShiftMask;
        if ((mods & HotkeyModifier.Alt)     != 0) x11Mods |= Mod1Mask;
        if ((mods & HotkeyModifier.Super)   != 0) x11Mods |= Mod4Mask;

        ulong keysym = MapVkToKeysym(vk);
        byte kc = XKeysymToKeycode(_display, keysym);
        if (kc == 0)
            throw new InvalidOperationException($"XKeysymToKeycode returned 0 for VK 0x{vk:X2}");

        _modifiers = x11Mods;
        _keycode = kc;

        // Grab × 4 for NumLock (Mod2) and CapsLock (Lock) toggle permutations.
        GrabKey(x11Mods);
        GrabKey(x11Mods | LockMask);
        GrabKey(x11Mods | Mod2Mask);
        GrabKey(x11Mods | LockMask | Mod2Mask);

        XSync(_display, 0);

        _running = true;
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "Stickies.X11Hotkey",
        };
        _eventThread.Start();
    }

    private void GrabKey(uint mods)
    {
        // owner_events=1, pointer_mode=GrabModeAsync(1), keyboard_mode=GrabModeAsync(1)
        XGrabKey(_display, _keycode, mods, _root, 1, 1, 1);
    }

    private void UngrabKey(uint mods) => XUngrabKey(_display, _keycode, mods, _root);

    private void EventLoop()
    {
        XEvent ev;
        while (_running)
        {
            if (XPending(_display) > 0)
            {
                XNextEvent(_display, &ev);
                if (ev.type == KeyPress && ev.keycode == _keycode)
                {
                    Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke());
                }
            }
            else
            {
                Thread.Sleep(20);
            }
        }
    }

    private static ulong MapVkToKeysym(uint vk) => vk switch
    {
        // X11 keysyms for ASCII letters are the lowercase ASCII codepoint.
        0x53 => 0x73, // S -> XK_s
        0x4E => 0x6E, // N -> XK_n
        _ => 0,
    };

    public void Dispose()
    {
        _running = false;
        _eventThread?.Join(500);
        if (_keycode != 0)
        {
            UngrabKey(_modifiers);
            UngrabKey(_modifiers | LockMask);
            UngrabKey(_modifiers | Mod2Mask);
            UngrabKey(_modifiers | LockMask | Mod2Mask);
            _keycode = 0;
        }
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
        }
    }

    [LibraryImport(Lib)]
    private static partial IntPtr XOpenDisplay(IntPtr displayName);

    [LibraryImport(Lib)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(Lib)]
    private static partial IntPtr XDefaultRootWindow(IntPtr display);

    [LibraryImport(Lib)]
    private static partial byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [LibraryImport(Lib)]
    private static partial int XGrabKey(IntPtr display, byte keycode, uint modifiers, IntPtr grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(Lib)]
    private static partial int XUngrabKey(IntPtr display, byte keycode, uint modifiers, IntPtr grabWindow);

    [LibraryImport(Lib)]
    private static partial int XPending(IntPtr display);

    [LibraryImport(Lib)]
    private static partial int XNextEvent(IntPtr display, XEvent* eventReturn);

    [LibraryImport(Lib)]
    private static partial int XSync(IntPtr display, int discard);
}
