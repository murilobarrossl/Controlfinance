using Npgsql;

namespace ControlFinance.API.Services;

public interface IJobLock
{
    /// <summary>Tenta o lock sem esperar. Nulo quando outra instância já o detém; descartar o
    /// handle libera o lock.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(long key, CancellationToken ct);
}

public static class JobLockKeys
{
    public const long ScheduledEmail = 0x4346_4A4F_0000_0001;
    public const long PendingDisconnectRetry = 0x4346_4A4F_0000_0002;
    public const long DataRetention = 0x4346_4A4F_0000_0003;
    public const long PolpInboxCleanup = 0x4346_4A4F_0000_0004;
    public const long PolpReconciliation = 0x4346_4A4F_0000_0005;
}

public static class JobLockExtensions
{
    public static async Task<bool> RunExclusiveAsync(this IJobLock jobLock, long key, Func<Task> round, CancellationToken ct)
    {
        await using var handle = await jobLock.TryAcquireAsync(key, ct);
        if (handle is null) return false;
        await round();
        return true;
    }
}

public sealed class PostgresJobLock(IConfiguration config, ILogger<PostgresJobLock> logger) : IJobLock
{
    private readonly string? _connectionString = config.GetConnectionString("DefaultConnection");

    // Lock de transação, não de sessão: no pooler em modo transação (Supabase, porta 6543) a sessão
    // do servidor muda entre comandos, mas fica fixa enquanto a transação estiver aberta. O
    // Postgres solta o lock no fim da transação, inclusive se a conexão cair no meio da rodada.
    public async Task<IAsyncDisposable?> TryAcquireAsync(long key, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_connectionString);
        NpgsqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(ct);
            transaction = await connection.BeginTransactionAsync(ct);

            // Transação parada durante a rodada inteira: um timeout de "idle in transaction"
            // configurado no servidor derrubaria a sessão e soltaria o lock com a rodada ainda rodando.
            await using (var timeout = new NpgsqlCommand("SET LOCAL idle_in_transaction_session_timeout = 0", connection, transaction))
                await timeout.ExecuteNonQueryAsync(ct);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction);
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct) is true)
                return new Handle(connection, transaction, key, logger);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }

        await transaction.DisposeAsync();
        await connection.DisposeAsync();
        return null;
    }

    private sealed class Handle(NpgsqlConnection connection, NpgsqlTransaction transaction, long key, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao encerrar a transação do lock {Key} ({ErrorType}); o Postgres solta o lock ao perder a conexão", key, ex.GetType().Name);
            }
            finally
            {
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
            }
        }
    }
}
