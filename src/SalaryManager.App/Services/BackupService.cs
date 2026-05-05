using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SalaryManager.App.Services;

public class BackupService
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "salary.db");

    public void Backup(string outputPath)
    {
        // VACUUM INTO produces an atomic, consistent copy regardless of journal mode
        // (DELETE/WAL). It also fails if the target file already exists, so clear
        // any existing backup first.
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path";
        cmd.Parameters.AddWithValue("$path", outputPath);
        cmd.ExecuteNonQuery();
    }

    public void Restore(string sourcePath)
    {
        File.Copy(sourcePath, _dbPath, overwrite: true);
        foreach (var ext in new[] { "-wal", "-shm" })
        {
            var side = _dbPath + ext;
            if (File.Exists(side)) File.Delete(side);
        }
    }
}
