using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Stickies.Platform;

namespace Stickies.Mac;

// Pattern A platform-specific: only constructed by Stickies.Platform.HotkeyHost.Create()
// when OperatingSystem.IsMacOS(). Carbon's RegisterEventHotKey + InstallEventHandler is
// the classic Mac global-hotkey API; still available on macOS 15 Sequoia, no entitlements
// or Accessibility permission needed.
//
// Modifier mapping is intentionally non-literal: HotkeyModifier.Control maps to cmdKey
// so the cross-platform call site Register(Control|Shift, 0x53) lands on the Mac-natural
// ⌘⇧S binding. HotkeyModifier.Super is mapped to controlKey for completeness.
//
// The Carbon callback fires on the Cocoa main thread (Carbon shares the main run loop
// with AppKit). We Dispatcher.UIThread.Post to be explicit about which thread the event
// fires on for callers.
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class HotkeyHost : IHotkeyHost
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint kEventHotKeyPressed = 5;

    private const uint cmdKey     = 0x0100;
    private const uint shiftKey   = 0x0200;
    private const uint optionKey  = 0x0800;
    private const uint controlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    private static HotkeyHost? s_instance;
    private IntPtr _hotkeyRef;
    private IntPtr _eventHandlerRef;

    public event Action? HotkeyPressed;

    public void Register(HotkeyModifier mods, uint vk)
    {
        if (_hotkeyRef != IntPtr.Zero) return;
        s_instance = this;

        uint carbonMods = 0;
        if ((mods & HotkeyModifier.Control) != 0) carbonMods |= cmdKey;
        if ((mods & HotkeyModifier.Shift)   != 0) carbonMods |= shiftKey;
        if ((mods & HotkeyModifier.Alt)     != 0) carbonMods |= optionKey;
        if ((mods & HotkeyModifier.Super)   != 0) carbonMods |= controlKey;

        uint kvk = MapVk(vk);

        var spec = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed };
        IntPtr handlerRef;
        int status = InstallEventHandler(
            GetApplicationEventTarget(),
            &Callback,
            1,
            &spec,
            IntPtr.Zero,
            &handlerRef);
        if (status != 0) throw new InvalidOperationException($"Carbon InstallEventHandler failed: {status}");
        _eventHandlerRef = handlerRef;

        var hkid = new EventHotKeyID { signature = 0x53544B59u /* 'STKY' */, id = 1 };
        IntPtr hkRef;
        status = RegisterEventHotKey(kvk, carbonMods, hkid, GetApplicationEventTarget(), 0, &hkRef);
        if (status != 0) throw new InvalidOperationException($"Carbon RegisterEventHotKey failed: {status}");
        _hotkeyRef = hkRef;
    }

    // Map Win32 VK to Carbon kVK_ANSI_*.
    private static uint MapVk(uint vk) => vk switch
    {
        0x53 => 0x01, // S -> kVK_ANSI_S
        0x4E => 0x2D, // N -> kVK_ANSI_N
        _ => 0,
    };

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Callback(IntPtr inHandlerCallRef, IntPtr inEvent, IntPtr inUserData)
    {
        var inst = s_instance;
        if (inst is not null)
        {
            Dispatcher.UIThread.Post(() => inst.HotkeyPressed?.Invoke());
        }
        return 0;
    }

    public void Dispose()
    {
        if (_hotkeyRef != IntPtr.Zero) { UnregisterEventHotKey(_hotkeyRef); _hotkeyRef = IntPtr.Zero; }
        if (_eventHandlerRef != IntPtr.Zero) { RemoveEventHandler(_eventHandlerRef); _eventHandlerRef = IntPtr.Zero; }
        s_instance = null;
    }

    [LibraryImport(Carbon)]
    private static partial IntPtr GetApplicationEventTarget();

    [LibraryImport(Carbon)]
    private static partial int InstallEventHandler(
        IntPtr inTarget,
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int> inHandler,
        uint inNumTypes,
        EventTypeSpec* inList,
        IntPtr inUserData,
        IntPtr* outRef);

    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(IntPtr inHandlerRef);

    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        IntPtr* outRef);

    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(IntPtr inHotKey);
}
