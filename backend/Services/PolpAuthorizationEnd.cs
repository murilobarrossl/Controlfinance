using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Autorização encerrada no banco (expirou, foi revogada lá ou recusada): o app para de usar os dados na
// hora (contas inativas somem de toda leitura) e os elimina depois da retenção, pelo mesmo caminho da
// desconexão voluntária. Esses status são terminais: o Connect não reaproveita a linha local.
public static class PolpAuthorizationEnd
{
    public static readonly string[] EndedStatuses = ["REVOKED", "EXPIRED", "REJECTED"];

    // Os 30 dias estão escritos no aviso de conexão, na Política e em Privacidade e dados.
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static bool IsEnded(string? status) => status is not null && EndedStatuses.Contains(status);

    public static async Task SuspendAsync(AppDbContext db, DateTime now, CancellationToken ct, Guid? integrationId = null)
    {
        var ended = db.PolpIntegrations.Where(p => EndedStatuses.Contains(p.Status));
        if (integrationId.HasValue) ended = ended.Where(p => p.Id == integrationId.Value);

        await ended.Where(p => p.AuthorizationEndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AuthorizationEndedAt, now), ct);
        await db.BankAccounts
            .Where(b => b.IsActive && b.PolpIntegrationId != null
                && ended.Any(p => p.PolpIntegrationId == b.PolpIntegrationId && p.UserId == b.UserId))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), ct);
    }
}
