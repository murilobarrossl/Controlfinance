using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<PolpIntegration> PolpIntegrations => Set<PolpIntegration>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // USER
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(150);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PhoneNumber).IsRequired().HasMaxLength(20);
            e.Property(u => u.Document).IsRequired().HasMaxLength(14);
            e.HasIndex(u => u.Document).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        // BANK ACCOUNT
        modelBuilder.Entity<BankAccount>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).IsRequired().HasMaxLength(100);
            e.Property(b => b.Balance).HasColumnType("numeric(18,2)");
            e.HasOne(b => b.User)
             .WithMany()
             .HasForeignKey(b => b.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // CATEGORY
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(80);
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // TRANSACTION
        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(150);
            e.Property(t => t.Amount).HasColumnType("numeric(18,2)");
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
        });

        // CREDIT CARD
        modelBuilder.Entity<CreditCard>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(80);
            e.Property(c => c.CreditLimit).HasColumnType("numeric(18,2)");
            e.Property(c => c.UsedLimit).HasColumnType("numeric(18,2)");
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // INSTALLMENT
        modelBuilder.Entity<Installment>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Description).IsRequired().HasMaxLength(150);
            e.Property(i => i.TotalAmount).HasColumnType("numeric(18,2)");
            e.Property(i => i.InstallmentAmount).HasColumnType("numeric(18,2)");
            e.HasOne(i => i.CreditCard)
             .WithMany(c => c.Installments)
             .HasForeignKey(i => i.CreditCardId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
