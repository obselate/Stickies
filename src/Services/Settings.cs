using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stickies.Services;

// ── Immutable value record ────────────────────────────────────────────────────

/// <summary>Snapshot of all user-configurable settings. Immutable; swap via Settings.Update.</summary>
public sealed record SettingsValues
{
    /// <summary>Days a soft-deleted note lingers before auto-purge. 0 = never purge.</summary>
    public int PurgeAfterDays { get; init; } = 30;
}

// ── JSON wire format ──────────────────────────────────────────────────────────

/// <summary>On-disk envelope: {"version":1,"settings":{...}}.</summary>
internal sealed class SettingsFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("settings")]
    public SettingsData Settings { get; set; } = new();
}

internal sealed class SettingsData
{
    [JsonPropertyName("purgeAfterDays")]
    public int? PurgeAfterDays { get; set; }
}

// ── AOT-safe source-gen context ───────────────────────────────────────────────

[JsonSerializable(typeof(SettingsFile))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext { }

// ── Singleton service ─────────────────────────────────────────────────────────

/// <summary>
/// Persistent user settings backed by %LOCALAPPDATA%\Stickies\settings.json.
/// Call <see cref="Load"/> once before any window opens. Read via <see cref="Current"/>;
/// mutate via <see cref="Update"/>.
/// </summary>
public static class Settings
{
    private static readonly string _dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stickies");

    private static readonly string _path = Path.Combine(_dir, "settings.json");
    private static readonly string _tmp  = Path.Combine(_dir, "settings.json.tmp");

    /// <summary>Current settings snapshot. Valid after <see cref="Load"/> returns.</summary>
    public static SettingsValues Current { get; private set; } = new();

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once in App.OnFrameworkInitializationCompleted before any window opens.
    /// On missing file: writes defaults. On corrupt JSON: logs to settings-error.txt,
    /// loads defaults, keeps running. Propagates unexpected exceptions (real bugs).
    /// </summary>
    public static void Load()
    {
        Directory.CreateDirectory(_dir);

        if (!File.Exists(_path))
        {
            Current = new SettingsValues();
            WriteDirect(Current);
            return;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var file = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.SettingsFile);
            Current = Hydrate(file);
        }
        catch (JsonException ex)
        {
            WriteErrorLog(ex);
            Current = new SettingsValues();
            // Overwrite the corrupt file with fresh defaults so next run is clean.
            WriteDirect(Current);
        }
        // Any other exception (e.g. IO permission) propagates — real bug.
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>Swaps <see cref="Current"/> and persists atomically.</summary>
    public static void Update(SettingsValues newValues)
    {
        Current = newValues;
        WriteDirect(newValues);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SettingsValues Hydrate(SettingsFile? file)
    {
        var defaults = new SettingsValues();
        if (file is null) return defaults;

        return new SettingsValues
        {
            // Per-key fallback: use compiled default when key is missing (null).
            PurgeAfterDays = file.Settings.PurgeAfterDays ?? defaults.PurgeAfterDays,
        };
    }

    private static void WriteDirect(SettingsValues values)
    {
        Directory.CreateDirectory(_dir);

        var file = new SettingsFile
        {
            Version = 1,
            Settings = new SettingsData
            {
                PurgeAfterDays = values.PurgeAfterDays,
            }
        };

        // Atomic write: tmp → rename.
        using (var stream = new FileStream(_tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, file, SettingsJsonContext.Default.SettingsFile);

        File.Move(_tmp, _path, overwrite: true);
    }

    private static void WriteErrorLog(Exception ex)
    {
        try
        {
            var errorPath = Path.Combine(_dir, "settings-error.txt");
            var entry = $"{DateTimeOffset.UtcNow:O} — {ex.GetType().Name}: {ex.Message}{Environment.NewLine}";
            File.WriteAllText(errorPath, entry);
        }
        catch
        {
            // Best-effort; never let error logging crash the app.
        }
    }
}
