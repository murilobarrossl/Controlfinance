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
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Conversores usados nas colunas que guardam dados sensíveis criptografados (AES-GCM).
        // A conversão é transparente: o resto do código sempre lê/escreve o valor em texto puro.
        var textConverter = new ValueConverter<string, string>(
            v => _encryption.Encrypt(v),
            v => _encryption.Decrypt(v));

        var decimalConverter = new ValueConverter<decimal, string>(
            v => _encryption.EncryptDecimal(v),
            v => _encryption.DecryptDecimal(v));

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(150);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PhoneNumber).IsRequired().HasMaxLength(20);

            // Document guarda o CPF/CNPJ criptografado: a busca/unicidade usa DocumentHash,
            // já que o ciphertext muda a cada gravação e não permite comparação direta.
            e.Property(u => u.Document).IsRequired().HasConversion(textConverter);
            e.Property(u => u.DocumentHash).IsRequired().HasMaxLength(100);
            e.HasIndex(u => u.DocumentHash).IsUnique();

            e.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BankAccount>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).IsRequired().HasMaxLength(100);
            e.Property(b => b.Balance).HasConversion(decimalConverter).HasColumnName("BalanceEncrypted");
            e.Property(b => b.Ownership).HasConversion<string>();
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
            e.Property(t => t.Name).IsRequired().HasMaxLength(150);
            e.Property(t => t.Amount).HasConversion(decimalConverter).HasColumnName("AmountEncrypted");
            e.Property(t => t.Type).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>();
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
            e.Property(i => i.Description).IsRequired().HasMaxLength(150);
            e.Property(i => i.TotalAmount).HasConversion(decimalConverter).HasColumnName("TotalAmountEncrypted");
            e.Property(i => i.InstallmentAmount).HasConversion(decimalConverter).HasColumnName("InstallmentAmountEncrypted");
            e.HasOne(i => i.CreditCard)
             .WithMany(c => c.Installments)
             .HasForeignKey(i => i.CreditCardId)
             .OnDelete(DeleteBehavior.SetNull);
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
