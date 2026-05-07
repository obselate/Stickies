using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stickies.Services;

namespace Stickies.Win32;

internal sealed partial class HotkeyHost : Window
{
    private const int HotkeyId = 1;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint VkN = 0x4E;
    private const uint WmHotkey = 0x0312;

    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private IntPtr _hwnd;
    private bool _registered;

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

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;
        _hwnd = handle.Handle;
        _registered = RegisterHotKey(_hwnd, HotkeyId, ModWin | ModShift, VkN);
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
            Dispatcher.UIThread.Post(() => NoteSpawner.SpawnNew(null));
        }
        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
