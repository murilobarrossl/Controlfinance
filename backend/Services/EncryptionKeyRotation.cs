using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Regrava toda coluna cifrada com a chave ativa (Encryption:ActiveKeyId). Só roda com
// Encryption:RotateOnStartup = true, ligado pelo operador numa troca de chave e desligado depois.
// Não dá pra saber pelo EF qual chave cifrou cada valor (o conversor entrega só o texto puro),
// então regrava tudo: marca as propriedades cifradas como modificadas e salva em lotes.
//
// Ordem segura de troca de chave (ver relatório, Parte H.1):
//   1. adicionar a chave nova em Encryption:Keys e apontar ActiveKeyId pra ela (a antiga fica);
//   2. ligar RotateOnStartup, subir a aplicação, conferir o log de conclusão, desligar;
//   3. só então remover a chave antiga da configuração. A partir daí, qualquer cópia antiga
//      (backup) cifrada com ela fica ilegível — descarte criptográfico intencional.
public static class EncryptionKeyRotation
{
    private const int BatchSize = 500;

    public static async Task RunAsync(AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!config.GetValue<bool>("Encryption:RotateOnStartup")) return;
        if (string.IsNullOrEmpty(config["Encryption:ActiveKeyId"]))
        {
            logger.LogWarning("Encryption:RotateOnStartup ligado sem Encryption:ActiveKeyId: nada a rotacionar.");
            return;
        }

        var total = 0;
        total += await RotateAsync<User>(db, ct);
        total += await RotateAsync<BankAccount>(db, ct);
        total += await RotateAsync<Transaction>(db, ct);
        total += await RotateAsync<CreditCard>(db, ct);
        total += await RotateAsync<Installment>(db, ct);
        total += await RotateAsync<AccessLogEntry>(db, ct);

        logger.LogInformation("Rotação de chave concluída: {Rows} linha(s) regravada(s) com a chave ativa. Desligue Encryption:RotateOnStartup.", total);
    }

    private static async Task<int> RotateAsync<T>(AppDbContext db, CancellationToken ct) where T : class
    {
        var entityType = db.Model.FindEntityType(typeof(T))!;
        // Propriedades cifradas = coluna com sufixo "Encrypted" (convenção do AppDbContext) e o
        // User.Document legado. Conversores de enum→string ficam de fora.
        var encrypted = entityType.GetProperties()
            .Where(p => p.GetValueConverter() is not null
                     && ((p.GetColumnName() ?? p.Name).EndsWith("Encrypted", StringComparison.Ordinal)
                         || (typeof(T) == typeof(User) && p.Name == nameof(User.Document))))
            .Select(p => p.Name)
            .ToList();
        if (encrypted.Count == 0) return 0;

        // Paginação por posição em ordem estável de Id: a rotação não insere nem apaga linhas,
        // então o deslocamento não "pula" registros entre lotes.
        var total = 0;
        while (true)
        {
            var batch = await db.Set<T>()
                .OrderBy(e => EF.Property<Guid>(e, "Id"))
                .Skip(total)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return total;

            foreach (var entity in batch)
            {
                var entry = db.Entry(entity);
                foreach (var name in encrypted)
                {
                    var property = entry.Property(name);
                    if (property.CurrentValue is not null) property.IsModified = true;
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }
}
