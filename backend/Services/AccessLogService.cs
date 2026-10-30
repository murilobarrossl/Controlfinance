using ControlFinance.API.Data;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Grava registro de acesso (ver AccessLogEntry) só quando AccessLog:Mode = "Authentication".
// Padrão "Off": a aplicação não guarda IP nem horário de acesso. Deve ser ligado assim que a
// empresa existir como pessoa jurídica e operar comercialmente (Marco Civil, art. 15) — ver
// relatório, Parte H.8.
public class AccessLogService(AppDbContext db, IConfiguration config)
{
    public bool Enabled => string.Equals(config["AccessLog:Mode"], "Authentication", StringComparison.OrdinalIgnoreCase);

    public async Task RecordAsync(Guid userId, string @event, string? ipAddress, CancellationToken ct = default)
    {
        if (!Enabled) return;

        db.AccessLogs.Add(new AccessLogEntry { UserId = userId, Event = @event, IpAddress = ipAddress });
        await db.SaveChangesAsync(ct);
    }
}
