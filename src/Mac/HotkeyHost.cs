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

    private const uint kEventParamDirectObject = 0x2D2D2D2Du; // '----'
    private const uint kTypeEventHotKeyID      = 0x686B6964u; // 'hkid'

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

    private readonly System.Collections.Generic.Dictionary<uint, (IntPtr hkRef, Action callback)> _callbacks = new();
    private static HotkeyHost? s_instance;
    private uint _nextId = 1;
    private IntPtr _eventHandlerRef;

    public void Register(HotkeyModifier mods, uint vk, Action callback)
    {
        s_instance = this;

        if (_eventHandlerRef == IntPtr.Zero)
        {
            var spec = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed };
            IntPtr handlerRef;
            int status = InstallEventHandler(
                GetApplicationEventTarget(), &Callback, 1, &spec, IntPtr.Zero, &handlerRef);
            if (status != 0) throw new InvalidOperationException($"Carbon InstallEventHandler failed: {status}");
            _eventHandlerRef = handlerRef;
        }

        uint carbonMods = 0;
        if ((mods & HotkeyModifier.Control) != 0) carbonMods |= cmdKey;
        if ((mods & HotkeyModifier.Shift)   != 0) carbonMods |= shiftKey;
        if ((mods & HotkeyModifier.Alt)     != 0) carbonMods |= optionKey;
        if ((mods & HotkeyModifier.Super)   != 0) carbonMods |= controlKey;

        uint kvk = MapVk(vk);
        uint id = _nextId++;
        var hkid = new EventHotKeyID { signature = 0x53544B59u /* 'STKY' */, id = id };
        IntPtr hkRef;
        int regStatus = RegisterEventHotKey(kvk, carbonMods, hkid, GetApplicationEventTarget(), 0, &hkRef);
        if (regStatus != 0) throw new InvalidOperationException($"Carbon RegisterEventHotKey failed: {regStatus}");

        _callbacks[id] = (hkRef, callback);
    }

    // Map Win32 VK to Carbon kVK_ANSI_*.
    private static uint MapVk(uint vk) => vk switch
    {
        0x53 => 0x01, // S -> kVK_ANSI_S
        0x4E => 0x2D, // N -> kVK_ANSI_N
        0x48 => 0x04, // H -> kVK_ANSI_H
        _ => 0,
    };

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Callback(IntPtr inHandlerCallRef, IntPtr inEvent, IntPtr inUserData)
    {
        var inst = s_instance;
        if (inst is null) return 0;

        EventHotKeyID hkid;
        int s = GetEventParameter(inEvent, kEventParamDirectObject, kTypeEventHotKeyID,
            IntPtr.Zero, (nuint)sizeof(EventHotKeyID), IntPtr.Zero, &hkid);
        if (s != 0) return 0;

        if (inst._callbacks.TryGetValue(hkid.id, out var entry))
            Dispatcher.UIThread.Post(entry.callback);
        return 0;
    }

    public void Dispose()
    {
        foreach (var entry in _callbacks.Values)
            UnregisterEventHotKey(entry.hkRef);
        _callbacks.Clear();
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

    // ByteCount is `unsigned long` on macOS (64-bit). Use nuint so the marshaller
    // emits the right ABI on arm64 / x86_64.
    [LibraryImport(Carbon)]
    private static partial int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        nuint inBufferSize,
        IntPtr outActualSize,
        EventHotKeyID* outData);
}
