using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ControlFinance.API.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly IEncryptionService _encryption;

    public AppDbContext(DbContextOptions<AppDbContext> options, IEncryptionService encryption) : base(options)
    {
        _encryption = encryption;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<PolpIntegration> PolpIntegrations => Set<PolpIntegration>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<RecurrenceDecision> RecurrenceDecisions => Set<RecurrenceDecision>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<PendingProviderDeletion> PendingProviderDeletions => Set<PendingProviderDeletion>();
    public DbSet<AccessLogEntry> AccessLogs => Set<AccessLogEntry>();
    public DbSet<PolpWebhookEvent> PolpWebhookEvents => Set<PolpWebhookEvent>();
    public DbSet<RemovedPolpResource> RemovedPolpResources => Set<RemovedPolpResource>();
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Conversores usados nas colunas que guardam dados sensíveis criptografados (AES-GCM).
        // A conversão é transparente: o resto do código sempre lê/escreve o valor em texto puro.
        var textConverter = new ValueConverter<string, string>(
            v => _encryption.Encrypt(v),
            v => _encryption.Decrypt(v));

        // Mesma criptografia do textConverter acima, mas nula-segura: BankAccount.Number (ao
        // contrário de User.Document) é opcional de verdade — nem toda conta sincronizada manda
        // um número, e Encrypt/Decrypt não aceitam null.
        var nullableTextConverter = new ValueConverter<string?, string?>(
            v => v == null ? null : _encryption.Encrypt(v),
            v => v == null ? null : _encryption.Decrypt(v));

        var decimalConverter = new ValueConverter<decimal, string>(
            v => _encryption.EncryptDecimal(v),
            v => _encryption.DecryptDecimal(v));

        // Nula-segura, mesmo motivo do nullableTextConverter: os campos de cartão em BankAccount
        // (CreditLimit/UsedLimit/CurrentInvoiceAmount) só existem quando a Polp manda esse dado
        // (ou o usuário completa manualmente) — a maioria das contas nunca vai ter valor aqui.
        var nullableDecimalConverter = new ValueConverter<decimal?, string?>(
            v => v == null ? null : _encryption.EncryptDecimal(v.Value),
            v => v == null ? null : _encryption.DecryptDecimal(v));

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(150);
            e.HasIndex(u => u.Email).IsUnique();
            // Document (legado) é o CPF/CNPJ cifrado de forma recuperável. Não é mais gravado
            // (ver User.Document); fica nulável só até PrivacyBackfill zerar as linhas antigas e
            // a coluna ser removida. Busca/unicidade sempre usaram DocumentHash.
            e.Property(u => u.Document).HasConversion(nullableTextConverter);
            e.Property(u => u.DocumentHash).IsRequired().HasMaxLength(100);
            e.HasIndex(u => u.DocumentHash).IsUnique();
            e.Property(u => u.DocumentType).HasMaxLength(4);
            e.Property(u => u.DueRemindersEnabled).HasDefaultValue(true);
            e.Property(u => u.TimeZone).IsRequired().HasMaxLength(64).HasDefaultValue(UserClock.DefaultTimeZoneId);

            e.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BankAccount>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).IsRequired().HasMaxLength(100);
            e.Property(b => b.Balance).HasConversion(nullableDecimalConverter).HasColumnName("BalanceEncrypted");
            e.HasIndex(b => b.UserId);
            e.HasIndex(b => new { b.UserId, b.IsActive });
            e.Property(b => b.Ownership).HasConversion<string>();
            // Number é dado sensível (mesmo nível de CPF/CNPJ), criptografado com o mesmo
            // conversor de User.Document. PolpAccountType/Subtype são só rótulos categóricos
            // ("BANK", "CREDIT"), ficam em texto puro, mesmo tratamento de BankCode.
            e.Property(b => b.Number).HasConversion(nullableTextConverter).HasColumnName("NumberEncrypted");
            // Campos de cartão (só preenchidos quando esta conta é reconhecida como cartão de
            // crédito): CreditLimit/UsedLimit/CurrentInvoiceAmount são valor monetário, mesmo
            // tratamento de Balance. ClosingDay/DueDay/InvoiceDueDate não são segredo, texto puro.
            e.Property(b => b.CreditLimit).HasConversion(nullableDecimalConverter).HasColumnName("CreditLimitEncrypted");
            e.Property(b => b.UsedLimit).HasConversion(nullableDecimalConverter).HasColumnName("UsedLimitEncrypted");
            e.Property(b => b.CurrentInvoiceAmount).HasConversion(nullableDecimalConverter).HasColumnName("CurrentInvoiceAmountEncrypted");
            e.HasOne(b => b.User)
             .WithMany()
             .HasForeignKey(b => b.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(80);
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Fecha a corrida que ResolveCategoryIdAsync (PolpSyncService) já sabe recuperar de:
            // duas execuções de sync concorrentes não conseguem mais criar duas categorias com o
            // mesmo nome pro mesmo usuário: a segunda leva 23505, e o catch já tratado busca a
            // que a primeira criou. ATENÇÃO AO APLICAR EM PRODUÇÃO: só depois de rodar a limpeza
            // dos 4 pares de categoria duplicada já existentes (ver plano da Fase C). A migration
            // desta constraint FALHA (Postgres não cria índice único sobre dado que já o viola)
            // se rodar antes da limpeza.
            e.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            // Name traz descrição do lançamento, nome de estabelecimento e, em transferências, o
            // nome da contraparte (dado pessoal de terceiro): cifrado em repouso como Amount.
            // Isso tira a busca/ordenação por nome do SQL (o ciphertext não é comparável): o
            // relatório faz essas duas em memória, sobre o mesmo teto de linhas que já usava pra
            // ordenar por valor. Nulável no banco só durante a transição: linhas antigas ainda
            // estão em texto puro na coluna "Name" (shadow property LegacyName) até
            // PrivacyBackfill cifrá-las e zerar a coluna antiga.
            e.Property(t => t.Name).HasConversion(textConverter).HasColumnName("NameEncrypted").IsRequired(false);
            e.Property<string?>("LegacyName").HasColumnName("Name").HasMaxLength(150);
            e.Property(t => t.Amount).HasConversion(decimalConverter).HasColumnName("AmountEncrypted");
            e.Property(t => t.Type).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.PolpMovementKind).HasConversion<string>().HasMaxLength(20);
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.BankAccount)
             .WithMany(b => b.Transactions)
             .HasForeignKey(t => t.BankAccountId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Category)
             .WithMany(c => c.Transactions)
             .HasForeignKey(t => t.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);

            // Cobre o filtro (usuário + intervalo de datas) usado no dashboard, em receitas x
            // despesas e nos relatórios: sem esse índice composto essas consultas caem num
            // scan por UserId seguido de ordenação em memória por DueDate.
            e.HasIndex(t => new { t.UserId, t.DueDate });
            e.HasIndex(t => new { t.UserId, t.BankAccountId, t.DueDate });
        });

        modelBuilder.Entity<CreditCard>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(80);
            e.Property(c => c.CreditLimit).HasConversion(decimalConverter).HasColumnName("CreditLimitEncrypted");
            e.Property(c => c.UsedLimit).HasConversion(decimalConverter).HasColumnName("UsedLimitEncrypted");
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Installment>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.UserId);
            // Descrição do parcelamento é digitada pelo usuário ("Notebook", "Tratamento dentário"…)
            // e pode revelar mais do que parece (inclusive saúde): cifrada como o nome das
            // transações. Mesma transição em duas fases: linhas antigas ficam em "Description"
            // (shadow LegacyDescription) até PrivacyBackfill cifrar.
            e.Property(i => i.Description).HasConversion(textConverter).HasColumnName("DescriptionEncrypted").IsRequired(false);
            e.Property<string?>("LegacyDescription").HasColumnName("Description").HasMaxLength(150);
            e.Property(i => i.TotalAmount).HasConversion(decimalConverter).HasColumnName("TotalAmountEncrypted");
            e.Property(i => i.InstallmentAmount).HasConversion(decimalConverter).HasColumnName("InstallmentAmountEncrypted");
            e.HasOne(i => i.CreditCard)
             .WithMany(c => c.Installments)
             .HasForeignKey(i => i.CreditCardId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.BankAccount)
             .WithMany()
             .HasForeignKey(i => i.BankAccountId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConsentRecord>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Kind).IsRequired().HasMaxLength(40);
            e.Property(c => c.Version).IsRequired().HasMaxLength(40);
            e.Property(c => c.Reference).HasMaxLength(40);
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.UserId, c.Kind });
        });

        modelBuilder.Entity<PendingProviderDeletion>(e =>
        {
            e.HasKey(p => p.Id);
        });

        // O webhook da Polp chega só com o id do recurso deles (consentimento, conta ou cartão):
        // é por esses índices que ele é resolvido para o usuário sem varrer a tabela. O
        // consentimento é único por usuário porque o POST /consents já é idempotente por
        // institution_id + documents (avoidDuplicates).
        modelBuilder.Entity<PolpIntegration>(e =>
        {
            e.HasIndex(p => p.PolpIntegrationId).IsUnique();
        });
        modelBuilder.Entity<BankAccount>(e =>
        {
            e.HasIndex(b => b.PolpAccountId);
        });

        modelBuilder.Entity<AccessLogEntry>(e =>
        {
            e.ToTable("AccessLogs");
            e.HasKey(a => a.Id);
            e.Property(a => a.Event).IsRequired().HasMaxLength(30);
            e.Property(a => a.IpAddress).HasConversion(nullableTextConverter).HasColumnName("IpAddressEncrypted");
            e.HasIndex(a => a.OccurredAt);
        });

        modelBuilder.Entity<PolpWebhookEvent>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Event).IsRequired().HasMaxLength(PolpWebhookEvent.EventMaxLength);
            e.Property(w => w.Resource).HasMaxLength(PolpWebhookEvent.ResourceMaxLength);
            e.Property(w => w.ResourceId).HasMaxLength(PolpWebhookEvent.ResourceIdMaxLength);
            e.Property(w => w.QueryParameters).HasMaxLength(PolpWebhookEvent.QueryParametersMaxLength);
            e.Property(w => w.DedupKey).IsRequired().HasMaxLength(PolpWebhookEvent.DedupKeyMaxLength);
            e.Property(w => w.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(w => w.LastError).HasMaxLength(PolpWebhookEvent.LastErrorMaxLength);
            // Dedupe só entre avisos ainda não processados: um aviso igual depois de Done precisa
            // virar linha nova, senão uma mudança real na Polp seria ignorada.
            e.HasIndex(w => w.DedupKey).IsUnique().HasFilter("\"Status\" IN ('Pending', 'Processing')");
            e.HasIndex(w => new { w.Status, w.NextAttemptAt });
            e.HasIndex(w => new { w.Status, w.Priority, w.NextAttemptAt });
        });

        modelBuilder.Entity<RemovedPolpResource>(e =>
        {
            e.HasKey(r => r.PolpId);
            e.Property(r => r.PolpId).ValueGeneratedNever();
        });

        modelBuilder.Entity<RevokedToken>(e =>
        {
            e.HasKey(r => r.Jti);
        });

        modelBuilder.Entity<RecurrenceDecision>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.NormalizedName).IsRequired().HasMaxLength(150);
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.AssumedFrequency).HasConversion<string>();
            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.BankAccount)
             .WithMany()
             .HasForeignKey(r => r.BankAccountId)
             .OnDelete(DeleteBehavior.Cascade);

            // Upsert (POST /api/recurrences/decision) depende dessa chave pra achar a linha
            // existente antes de decidir entre criar e atualizar.
            e.HasIndex(r => new { r.UserId, r.BankAccountId, r.NormalizedName }).IsUnique();
        });
    }
}
