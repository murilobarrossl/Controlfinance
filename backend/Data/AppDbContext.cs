using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Name)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(u => u.Email)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.HasIndex(u => u.Email)
                  .IsUnique();

            entity.Property(u => u.PhoneNumber)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.Property(u => u.Document)
                  .IsRequired()
                  .HasMaxLength(14);

            entity.HasIndex(u => u.Document)
                  .IsUnique();

            entity.Property(u => u.PasswordHash)
                  .IsRequired();
        });
    }
}
