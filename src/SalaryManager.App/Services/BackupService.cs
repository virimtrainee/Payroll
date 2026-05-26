using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SalaryManager.App.Services;

public class BackupService
{
    private readonly string _dbPath = AppPaths.DatabasePath;

    public void Backup(string outputPath)
    {
        if (SamePath(outputPath, _dbPath))
            throw new InvalidOperationException("Choose a backup path that is different from the live database.");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // VACUUM INTO produces an atomic, consistent copy regardless of journal mode
        // (DELETE/WAL). It also fails if the target file already exists, so clear
        // any existing backup first.
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var conn = new SqliteConnection(ConnectionString(_dbPath));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path";
        cmd.Parameters.AddWithValue("$path", outputPath);
        cmd.ExecuteNonQuery();
    }

    public void Restore(string sourcePath)
    {
        ValidateSqliteDatabase(sourcePath);
        Directory.CreateDirectory(AppPaths.DataDirectory);
        DeleteSidecars();
        File.Copy(sourcePath, _dbPath, overwrite: true);
        DeleteSidecars();
    }

    private void DeleteSidecars()
    {
        foreach (var ext in new[] { "-wal", "-shm" })
        {
            var side = _dbPath + ext;
            if (File.Exists(side)) File.Delete(side);
        }
    }

    private static void ValidateSqliteDatabase(string path)
    {
        using var conn = new SqliteConnection(ConnectionString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        var result = cmd.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected file is not a valid SQLite database backup.");
    }

    private static string ConnectionString(string path)
        => new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
