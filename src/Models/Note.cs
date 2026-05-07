namespace Stickies.Models;

public sealed record Note(
    long Id,
    string Text,
    int? X,
    int? Y,
    int Width,
    int Height,
    bool Pinned,
    string Color,
    bool Locked);
