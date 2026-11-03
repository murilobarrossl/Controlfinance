using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Trata o dado que já existia antes da migration PrivacyHardening. Roda a cada inicialização,
// mas é idempotente e barato quando não há nada pendente (cada etapa só pega linhas que ainda
// estão no formato antigo). Diferente do backfill antigo do Radar, não depende de "a migration
// estava pendente": se o processo cair no meio, a próxima inicialização continua de onde parou,
// em vez de deixar dado em texto puro pra trás pra sempre.
public static class PrivacyBackfill
{
    private const int BatchSize = 500;

    public static async Task RunAsync(AppDbContext db, IEncryptionService encryption, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        var users = await StripRecoverableDocumentsAsync(db, ct);
        var transactions = await EncryptLegacyTransactionNamesAsync(db, ct);
        var accounts = await MaskAccountNumbersAsync(db, ct);
        var installments = await EncryptLegacyInstallmentDescriptionsAsync(db, ct);

        // Irreversível (o texto puro do nome normalizado some), por isso só com opt-in do
        // operador. Sem rodar, nada quebra: RecurrenceKey reconhece também a chave antiga.
        var recurrences = config.GetValue<bool>("Privacy:HashLegacyRecurrenceNames")
            ? await HashLegacyRecurrenceNamesAsync(db, encryption, ct)
            : 0;

        // Só contagens: nenhum dado do usuário vai pro log.
        if (users + transactions + accounts + installments + recurrences > 0)
            logger.LogInformation(
                "PrivacyBackfill: {Users} documento(s) reduzido(s) a tipo, {Transactions} nome(s) de transação cifrado(s), {Accounts} número(s) de conta mascarado(s), {Installments} descrição(ões) de parcelamento cifrada(s), {Recurrences} chave(s) de recorrência convertida(s) em hash.",
                users, transactions, accounts, installments, recurrences);
    }

    // Descrição de parcelamento em texto puro (coluna "Description", shadow LegacyDescription) →
    // DescriptionEncrypted. Reversível: o valor continua decifrável com a chave.
    private static async Task<int> EncryptLegacyInstallmentDescriptionsAsync(AppDbContext db, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Installments
                .Where(i => EF.Property<string?>(i, "LegacyDescription") != null)
                .OrderBy(i => i.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return total;

            foreach (var installment in batch)
            {
                var legacy = db.Entry(installment).Property<string?>("LegacyDescription");
                if (string.IsNullOrEmpty(installment.Description))
                    installment.Description = legacy.CurrentValue ?? string.Empty;
                legacy.CurrentValue = null;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    private static async Task<int> HashLegacyRecurrenceNamesAsync(AppDbContext db, IEncryptionService encryption, CancellationToken ct)
    {
        var legacy = await db.RecurrenceDecisions
            .Where(d => !d.NormalizedName.StartsWith(RecurrenceKey.Prefix))
            .ToListAsync(ct);

        foreach (var decision in legacy)
            decision.NormalizedName = RecurrenceKey.For(encryption, decision.NormalizedName);

        if (legacy.Count > 0) await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return legacy.Count;
    }

    // CPF/CNPJ recuperável → só o tipo (CPF/CNPJ). DocumentHash continua cobrindo login,
    // unicidade e a conferência do documento digitado na conexão bancária.
    private static async Task<int> StripRecoverableDocumentsAsync(AppDbContext db, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Users.Where(u => u.Document != null).OrderBy(u => u.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) return total;

            foreach (var user in batch)
            {
                var digits = new string(user.Document!.Where(char.IsDigit).ToArray());
                user.DocumentType ??= DocumentTypes.FromDigits(digits);
                user.Document = null;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // Nome em texto puro (coluna antiga "Name", shadow property LegacyName) → NameEncrypted.
    private static async Task<int> EncryptLegacyTransactionNamesAsync(AppDbContext db, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Transactions
                .Where(t => EF.Property<string?>(t, "LegacyName") != null)
                .OrderBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return total;

            foreach (var transaction in batch)
            {
                var legacy = db.Entry(transaction).Property<string?>("LegacyName");
                // Se já existir nome cifrado (ex.: editado depois da migration), ele é o mais
                // recente e prevalece; a cópia antiga em texto puro só é descartada.
                if (string.IsNullOrEmpty(transaction.Name))
                    transaction.Name = legacy.CurrentValue ?? string.Empty;
                legacy.CurrentValue = null;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // Número de conta/cartão completo → "•••• 1234". O valor está cifrado, então a checagem de
    // "já está mascarado" só dá pra fazer em memória.
    private static async Task<int> MaskAccountNumbersAsync(AppDbContext db, CancellationToken ct)
    {
        var accounts = await db.BankAccounts.Where(b => b.Number != null).ToListAsync(ct);
        var changed = 0;

        foreach (var account in accounts)
        {
            var masked = AccountNumberMask.Mask(account.Number);
            if (masked == account.Number) continue;
            account.Number = masked;
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return changed;
    }
}
