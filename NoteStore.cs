using System;
using System.Collections.Generic;
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
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS notes (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    text       TEXT    NOT NULL DEFAULT '',
                    x          INTEGER,
                    y          INTEGER,
                    width      INTEGER NOT NULL DEFAULT 280,
                    height     INTEGER NOT NULL DEFAULT 280,
                    updated_at INTEGER NOT NULL,
                    deleted_at INTEGER
                );
            """;
            cmd.ExecuteNonQuery();
        }

        AddColumnIfMissing(c, "x", "INTEGER");
        AddColumnIfMissing(c, "y", "INTEGER");
        AddColumnIfMissing(c, "width", "INTEGER NOT NULL DEFAULT 280");
        AddColumnIfMissing(c, "height", "INTEGER NOT NULL DEFAULT 280");
        AddColumnIfMissing(c, "deleted_at", "INTEGER");
        AddColumnIfMissing(c, "pinned", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string col, string type)
    {
        using var info = c.CreateCommand();
        info.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name=@n;";
        info.Parameters.Add("@n", SqliteType.Text).Value = col;
        var exists = Convert.ToInt64(info.ExecuteScalar() ?? 0L) > 0;
        if (exists) return;

        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE notes ADD COLUMN {col} {type};";
        alter.ExecuteNonQuery();
    }

    public sealed record NoteRow(long Id, string Text, int? X, int? Y, int Width, int Height, bool Pinned);

    private const string SelectColumns = "id, text, x, y, width, height, pinned";

    private static NoteRow ReadRow(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.IsDBNull(2) ? null : r.GetInt32(2),
        r.IsDBNull(3) ? null : r.GetInt32(3),
        r.GetInt32(4),
        r.GetInt32(5),
        r.GetInt32(6) != 0);

    public List<NoteRow> LoadActive()
    {
        var list = new List<NoteRow>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM notes WHERE deleted_at IS NULL ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
        return list;
    }

    public NoteRow? LoadById(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM notes WHERE id=@id AND deleted_at IS NULL;";
        cmd.Parameters.Add("@id", SqliteType.Integer).Value = id;
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    public NoteRow Create(int? x, int? y, int width, int height)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes (text, x, y, width, height, updated_at)
            VALUES ('', @x, @y, @w, @h, @ts);
            SELECT last_insert_rowid();
        """;
        cmd.Parameters.Add("@x", SqliteType.Integer).Value = (object?)x ?? DBNull.Value;
        cmd.Parameters.Add("@y", SqliteType.Integer).Value = (object?)y ?? DBNull.Value;
        cmd.Parameters.Add("@w", SqliteType.Integer).Value = width;
        cmd.Parameters.Add("@h", SqliteType.Integer).Value = height;
        cmd.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        return new NoteRow(id, "", x, y, width, height, true);
    }

    public void UpdatePinned(long id, bool pinned)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE notes SET pinned=@p, updated_at=@ts WHERE id=@id;";
        cmd.Parameters.Add("@p", SqliteType.Integer).Value = pinned ? 1 : 0;
        cmd.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        cmd.Parameters.Add("@id", SqliteType.Integer).Value = id;
        cmd.ExecuteNonQuery();
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

    public void UpdateBounds(long id, int x, int y, int width, int height)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE notes SET x=@x, y=@y, width=@w, height=@h, updated_at=@ts WHERE id=@id;";
        cmd.Parameters.Add("@x", SqliteType.Integer).Value = x;
        cmd.Parameters.Add("@y", SqliteType.Integer).Value = y;
        cmd.Parameters.Add("@w", SqliteType.Integer).Value = width;
        cmd.Parameters.Add("@h", SqliteType.Integer).Value = height;
        cmd.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        cmd.Parameters.Add("@id", SqliteType.Integer).Value = id;
        cmd.ExecuteNonQuery();
    }

    public void SoftDelete(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE notes SET deleted_at=@ts WHERE id=@id;";
        cmd.Parameters.Add("@ts", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        cmd.Parameters.Add("@id", SqliteType.Integer).Value = id;
        cmd.ExecuteNonQuery();
    }
}
