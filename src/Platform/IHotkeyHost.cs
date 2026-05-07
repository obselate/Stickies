using System;

namespace Stickies.Platform;

internal interface IHotkeyHost : IDisposable
{
    void Register(HotkeyModifier mods, uint vk, Action callback);
}
