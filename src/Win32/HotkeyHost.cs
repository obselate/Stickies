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
    private const uint WmHotkey = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;

    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private IntPtr _hwnd;

    private readonly System.Collections.Generic.Dictionary<int, Action> _callbacks = new();
    private readonly System.Collections.Generic.List<(int id, uint mods, uint vk)> _pending = new();
    private int _nextId = 1;

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

    public void Register(HotkeyModifier mods, uint vk, Action callback)
    {
        int id = _nextId++;
        uint w32mods = ToWin32(mods);
        _callbacks[id] = callback;
        if (_hwnd != IntPtr.Zero) RegisterHotKey(_hwnd, id, w32mods, vk);
        else _pending.Add((id, w32mods, vk));
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
        foreach (var p in _pending) RegisterHotKey(_hwnd, p.id, p.mods, p.vk);
        _pending.Clear();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        foreach (var id in _callbacks.Keys) UnregisterHotKey(_hwnd, id);
        _callbacks.Clear();
    }

    private IntPtr WndProcHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var cb))
            {
                handled = true;
                Dispatcher.UIThread.Post(cb);
            }
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
