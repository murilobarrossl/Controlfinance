using System.Globalization;
using System.Text.RegularExpressions;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static partial class BankBalanceHistory
{
    // updated_at é data do recurso na Polp, não data do saldo no banco. Não serve
    // de fallback. Também não atribuímos o fuso do servidor a uma data sem offset.
    public static DateTime? BankTimestamp(PolpBalanceDto? balance, DateTimeOffset now)
    {
        var text = balance?.UpdateDateTime;
        return text is not null && text.Contains('T') && OffsetSuffix().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            && instant.Year > 1 && instant <= now
                ? instant.UtcDateTime : null;
    }

    // Chamado sob IIntegrationLock. O histórico empresarial guarda apenas observações
    // recebidas; reenvios corrigem o mesmo instante, sem fabricar pontos entre leituras.
    public static async Task ApplyAsync(AppDbContext db, BankAccount account, PolpAccountDto remote,
        string? documentType, IConfiguration config, DateTimeOffset now, CancellationToken ct)
    {
        if (remote.Id != account.PolpAccountId || remote.ConsentId != account.PolpIntegrationId)
            throw new PolpPermanentException("Saldo de outro recurso na resposta da Polp.");
        if (remote.Balance?.AvailableAmount?.Value is not { } value) return;
        if (documentType != DocumentTypes.Cnpj)
        {
            account.Balance = value;
            return;
        }

        var instant = BankTimestamp(remote.Balance, now);
        // O app exibe reais: não registrar uma posição de outra moeda como BRL.
        var brl = string.Equals(remote.Balance.AvailableAmount.Currency, "BRL", StringComparison.OrdinalIgnoreCase);
        if (!brl) return;
        if (account.BalanceUpdatedAt is null || instant is null || instant >= account.BalanceUpdatedAt)
        {
            account.Balance = value;
            account.BalanceUpdatedAt = instant;
        }
        var cutoff = DataRetentionPolicy.SyncedHistoryCutoff(config, now.UtcDateTime);
        if (instant is null || (cutoff.HasValue && instant < cutoff)) return;

        var observation = await db.BankBalanceObservations.FirstOrDefaultAsync(x => x.UserId == account.UserId
            && x.BankAccountId == account.Id && x.ObservedAt == instant.Value, ct);
        if (observation is null)
            db.BankBalanceObservations.Add(new BankBalanceObservation
            {
                UserId = account.UserId, BankAccountId = account.Id, ObservedAt = instant.Value, Balance = value
            });
        else observation.Balance = value;
    }

    [GeneratedRegex(@"(?:[zZ]|[+\-]\d{2}:\d{2})$")]
    private static partial Regex OffsetSuffix();
}
