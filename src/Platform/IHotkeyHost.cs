using System;

namespace Stickies.Platform;

internal interface IHotkeyHost : IDisposable
{
    event Action HotkeyPressed;
    void Register(HotkeyModifier mods, uint vk);
}
