using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stickies.Platform;

namespace Stickies.Win32;

[SupportedOSPlatform("windows")]
internal sealed partial class HotkeyHost : Window, IHotkeyHost
{
    private const int HotkeyId = 1;
    private const uint WmHotkey = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;

    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private IntPtr _hwnd;
    private bool _registered;
    private uint _pendingMods;
    private uint _pendingVk;

    public event Action? HotkeyPressed;

    public HotkeyHost()
    {
        Width = 1;
        Height = 1;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Opacity = 0;
        Position = new PixelPoint(-32000, -32000);
        Title = "Stickies.HotkeyHost";

        _hook = WndProcHook;
        Win32Properties.AddWndProcHookCallback(this, _hook);

        Opened += OnOpened;
        Closing += OnClosing;
    }

    public void Register(HotkeyModifier mods, uint vk)
    {
        _pendingMods = ToWin32(mods);
        _pendingVk = vk;
        if (_hwnd != IntPtr.Zero) DoRegister();
    }

    private static uint ToWin32(HotkeyModifier m)
    {
        uint r = 0;
        if ((m & HotkeyModifier.Control) != 0) r |= MOD_CONTROL;
        if ((m & HotkeyModifier.Shift) != 0) r |= MOD_SHIFT;
        if ((m & HotkeyModifier.Alt) != 0) r |= MOD_ALT;
        if ((m & HotkeyModifier.Super) != 0) r |= MOD_WIN;
        return r;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;
        _hwnd = handle.Handle;
        if (_pendingVk != 0) DoRegister();
    }

    private void DoRegister()
    {
        if (_registered) return;
        _registered = RegisterHotKey(_hwnd, HotkeyId, _pendingMods, _pendingVk);
        // If registration fails (another app holds the combo) we silently continue.
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke());
        }
        return IntPtr.Zero;
    }

    void IDisposable.Dispose() => Close();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
