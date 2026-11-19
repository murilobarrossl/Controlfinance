using System.Reflection;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlFinance.API.Tests;

// O EF aplica migrations em ordem de ID, e vários IDs existentes foram escritos com data futura (até
// 20/11/2026). Uma migration gerada antes dessa hora recebe ID menor: num banco novo rodaria no meio da
// cadeia, e `migrations remove` apagaria outra. Renomeie a migration nova (ainda não aplicada) para um ID
// maior que LastExistingId.
public class MigrationOrderTests
{
    private const string LastExistingId = "20261120050000_InactivityDeletionClaim";
    private const int ExistingCount = 85;

    [Fact]
    public void New_migrations_sort_after_every_existing_one()
    {
        var ids = typeof(AppDbContext).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .ToList();

        Assert.Equal(ExistingCount, ids.Count(id => string.CompareOrdinal(id, LastExistingId) <= 0));
    }

    [Fact]
    public void Last_migration_keeps_its_target_model_for_migrations_remove()
    {
        var last = typeof(AppDbContext).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<MigrationAttribute>() is not null)
            .OrderBy(t => t.GetCustomAttribute<MigrationAttribute>()!.Id, StringComparer.Ordinal)
            .Last();

        var migration = (Migration)Activator.CreateInstance(last)!;
        Assert.NotNull(migration.TargetModel);
    }
}
