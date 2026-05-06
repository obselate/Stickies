using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Stickies;

public sealed class NoteStore
{
    private readonly string _connStr;

    public NoteStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Stickies");
        Directory.CreateDirectory(dir);
        _connStr = $"Data Source={Path.Combine(dir, "notes.db")};";
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        return c;
    }

    private void EnsureSchema()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS notes (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                text       TEXT    NOT NULL DEFAULT '',
                updated_at INTEGER NOT NULL
            );
        """;
        cmd.ExecuteNonQuery();
    }

    public (long id, string text) LoadOrCreateDefault()
    {
        using var c = Open();

        using (var sel = c.CreateCommand())
        {
            sel.CommandText = "SELECT id, text FROM notes ORDER BY id LIMIT 1;";
            using var r = sel.ExecuteReader();
            if (r.Read()) return (r.GetInt64(0), r.GetString(1));
        }

        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO notes (text, updated_at) VALUES ('', @ts); SELECT last_insert_rowid();";
        ins.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = (long)(ins.ExecuteScalar() ?? 0L);
        return (id, string.Empty);
    }

    public void UpdateText(long id, string text)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE notes SET text=@t, updated_at=@ts WHERE id=@id;";
        cmd.Parameters.Add("@t", SqliteType.Text).Value = text;
        cmd.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        cmd.Parameters.Add("@id", SqliteType.Integer).Value = id;
        cmd.ExecuteNonQuery();
    }
}
