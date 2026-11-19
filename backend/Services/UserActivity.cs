using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

/// <summary>A atividade do usuário não pôde ser confirmada no banco: a requisição não é atendida (503).</summary>
public sealed class ActivityNotRecordedException(Exception inner) : Exception("Atividade do usuário não registrada.", inner);

// Atividade real do usuário para a exclusão por inatividade (InactiveAccountService). Só é chamada por
// login e por requisição autenticada com sessão válida; webhooks, jobs e e-mails nunca passam aqui.
// Falha fechado: uso sem atividade gravada poderia virar exclusão de quem está usando, então quem não
// consegue registrar a atividade não é atendido. O cache local só evita escrita repetida e só é marcado
// depois de o banco confirmar uma atividade de no máximo Granularity atrás; a rotina de exclusão nunca o lê.
public static class UserActivity
{
    public static readonly TimeSpan Granularity = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private static string Key(Guid userId) => $"user-activity:{userId}";

    public static async Task TouchAsync(AppDbContext db, IMemoryCache cache, Guid userId, DateTime utcNow, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(userId), out _)) return;
        await RecordWithRetryAsync(db, userId, utcNow, ct);
        cache.Set(Key(userId), true, Granularity);
    }

    public static async Task RecordWithRetryAsync(AppDbContext db, Guid userId, DateTime utcNow, CancellationToken ct)
    {
        try
        {
            await RecordAsync(db, userId, utcNow, ct);
        }
        catch (Exception ex) when (DatabaseFailure.IsTransient(ex) && !ct.IsCancellationRequested)
        {
            await Task.Delay(RetryDelay, ct);
            try
            {
                await RecordAsync(db, userId, utcNow, ct);
            }
            catch (Exception retry) when (retry is not OperationCanceledException)
            {
                throw new ActivityNotRecordedException(retry);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ActivityNotRecordedException(ex);
        }
    }

    // Sem condição de IsActive: atividade que chegue numa conta já reservada para exclusão precisa ficar
    // gravada, para a confirmação final (InactiveAccountService) cancelar a exclusão. Exclusão já iniciada
    // não é desfeita: zerar a sequência ali tiraria a conta da retomada.
    private static Task<int> RecordAsync(AppDbContext db, Guid userId, DateTime utcNow, CancellationToken ct)
    {
        var stale = utcNow - Granularity;
        return db.Users
            .Where(u => u.Id == userId && u.LastActivityAt < stale
                && u.InactivityNoticeStage != InactiveAccountService.DeletionStartedStage)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastActivityAt, utcNow)
                .SetProperty(u => u.InactivityNoticeStage, 0)
                .SetProperty(u => u.InactivityNoticeEpoch, (DateTime?)null)
                .SetProperty(u => u.InactivityDeletionAt, (DateTime?)null), ct);
    }
}
