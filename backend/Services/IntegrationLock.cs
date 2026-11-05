using System.Collections.Concurrent;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public interface IIntegrationLock
{
    /// <summary>Roda <paramref name="work"/> numa transação do <paramref name="db"/>, exclusiva
    /// por integração (PolpIntegration.Id) entre todas as instâncias. Commit só se work terminar
    /// sem exceção. Nunca chamar a Polp dentro de work: a transação tem de ser curta.</summary>
    Task<T> RunAsync<T>(AppDbContext db, Guid localIntegrationId, Func<Task<T>> work, CancellationToken ct);
}

public sealed class IntegrationLock : IIntegrationLock
{
    // "CFIL": separa estas chaves (int, int) das de JobLockKeys (bigint), que ocupam outro espaço.
    private const int ClassId = 0x4346_494C;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _local = new();

    public Task<T> RunAsync<T>(AppDbContext db, Guid localIntegrationId, Func<Task<T>> work, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? RunPostgresAsync(db, localIntegrationId, work, ct)
            : RunInProcessAsync(db, localIntegrationId, work, ct);

    public static int ObjectId(Guid id)
    {
        var bytes = id.ToByteArray();
        return BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4)
            ^ BitConverter.ToInt32(bytes, 8) ^ BitConverter.ToInt32(bytes, 12);
    }

    // Lock de transação: some no commit/rollback e também se a conexão cair, e funciona no pooler
    // em modo transação. Colisão do hash de 4 bytes só serializa duas integrações à toa.
    private static async Task<T> RunPostgresAsync<T>(AppDbContext db, Guid id, Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Sem teto, um lock preso deixaria o worker parado; estourando, o evento volta para a fila.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '60s'", ct);
        var objectId = ObjectId(id);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ClassId}, {objectId})", ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    // Só para bancos que não são Postgres (testes com SQLite): exclusão dentro deste processo.
    private async Task<T> RunInProcessAsync<T>(AppDbContext db, Guid id, Func<Task<T>> work, CancellationToken ct)
    {
        var gate = _local.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
