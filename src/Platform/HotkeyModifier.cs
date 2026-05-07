using System;

namespace Stickies.Platform;

[Flags]
internal enum HotkeyModifier
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Super = 8,
}
