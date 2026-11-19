using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace ControlFinance.API.Data;

// Npgsql 8.0.4 checks existence by opening a new, unpooled connection. Startup
// already holds an authenticated connection to this database. Reuse that evidence
// instead of requiring another network handshake for each history/existence check.
// No schema check or migration execution is bypassed; subsequent SQL still must succeed.
// This extension depends on the pinned provider API: review it when upgrading Npgsql EF.
#pragma warning disable EF1001
public sealed class ConnectedNpgsqlDatabaseCreator : NpgsqlDatabaseCreator
{
    private readonly INpgsqlRelationalConnection _connection;

    public ConnectedNpgsqlDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        INpgsqlRelationalConnection connection,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : base(dependencies, connection, rawSqlCommandBuilder)
    {
        _connection = connection;
    }

    public override bool Exists() =>
        _connection.DbConnection.State == ConnectionState.Open || base.Exists();

    public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _connection.DbConnection.State == ConnectionState.Open
            ? Task.FromResult(true)
            : base.ExistsAsync(cancellationToken);
    }
}
#pragma warning restore EF1001
