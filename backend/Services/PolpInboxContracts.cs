namespace ControlFinance.API.Services;

// Ordem de claim: High (avisos da Polp e o backfill de quem acabou de conectar) sempre antes de
// Low (reconciliação), que é só rede de segurança e não pode atrasar um aviso real.
public enum PolpInboxPriority
{
    High = 0,
    Low = 1
}

public sealed record PolpInboxEvent(
    string Event, string? Resource, string? ResourceId, string? QueryParameters,
    PolpInboxPriority Priority = PolpInboxPriority.High);

public interface IPolpInbox
{
    /// <summary>Grava o evento na inbox. Devolve false se já havia um igual pendente ou em processamento.</summary>
    Task<bool> EnqueueAsync(PolpInboxEvent evt, CancellationToken ct);
}

public static class PolpInternalEvents
{
    public const string Backfill = "internal.backfill";
    public const string Reconcile = "internal.reconcile";
}

/// <summary>Não adianta tentar de novo: o evento vai para Dead.</summary>
public sealed class PolpPermanentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>O recurso ainda não existe localmente (ex.: transações antes da conta): tentar de novo mais tarde.</summary>
public sealed class PolpResourceNotReadyException(string message) : Exception(message);
