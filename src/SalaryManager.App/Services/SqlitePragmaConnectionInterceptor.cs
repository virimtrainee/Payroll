using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SalaryManager.App.Services;

internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    private const string RuntimePragmas = "PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyRuntimePragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = RuntimePragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ApplyRuntimePragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection)
            return;

        using var command = connection.CreateCommand();
        command.CommandText = RuntimePragmas;
        command.ExecuteNonQuery();
    }
}
