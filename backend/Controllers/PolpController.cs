using System.Security.Claims;
using ControlFinance.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/polp")]
[Authorize]
public class PolpController(AppDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/polp/connectors
    [HttpGet("connectors")]
    public IActionResult GetConnectors()
    {
        var connectors = new[]
        {
            new { id = "001", name = "Banco do Brasil",  color = "#F9C200", textColor = "#003882", initials = "BB"  },
            new { id = "260", name = "Nubank",           color = "#820AD1", textColor = "#fff",    initials = "Nu"  },
            new { id = "341", name = "Itaú",             color = "#EC7000", textColor = "#fff",    initials = "It"  },
            new { id = "033", name = "Santander",        color = "#CC0000", textColor = "#fff",    initials = "San" },
            new { id = "336", name = "C6 Bank",          color = "#000",    textColor = "#fff",    initials = "C6"  },
            new { id = "237", name = "Bradesco",         color = "#CC092F", textColor = "#fff",    initials = "Bra" },
            new { id = "077", name = "Inter",            color = "#FF6B00", textColor = "#fff",    initials = "Int" },
            new { id = "104", name = "Caixa Econômica",  color = "#005CA9", textColor = "#fff",    initials = "CEF" },
            new { id = "422", name = "Safra",            color = "#1B3A6B", textColor = "#fff",    initials = "Saf" },
            new { id = "290", name = "PagBank",          color = "#00C851", textColor = "#fff",    initials = "Pag" },
        };
        return Ok(connectors);
    }

    // POST /api/polp/integrations
    [HttpPost("integrations")]
    public async Task<IActionResult> Connect([FromBody] PolpConnectDto dto)
    {
        await Task.Delay(1500);

        var bankNames = new Dictionary<string, string>
        {
            ["001"] = "Banco do Brasil", ["260"] = "Nubank",   ["341"] = "Itaú",
            ["033"] = "Santander",       ["336"] = "C6 Bank",  ["237"] = "Bradesco",
            ["077"] = "Inter",           ["104"] = "Caixa Econômica",
            ["422"] = "Safra",           ["290"] = "PagBank",
        };

        var bankName = bankNames.TryGetValue(dto.ConnectorId, out var n) ? n : "Banco";

        var account = new ControlFinance.API.Models.BankAccount
        {
            UserId   = UserId,
            Name     = $"{bankName} - Conta Corrente",
            BankCode = dto.ConnectorId,
            Balance  = 4250.75m
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        // Categorias padrão se não existirem
        if (!db.Categories.Any(c => c.UserId == UserId))
        {
            db.Categories.AddRange(
                new ControlFinance.API.Models.Category { UserId = UserId, Name = "Alimentação",  Color = "#FF6B6B" },
                new ControlFinance.API.Models.Category { UserId = UserId, Name = "Transporte",   Color = "#4ECDC4" },
                new ControlFinance.API.Models.Category { UserId = UserId, Name = "Salário",      Color = "#45B7D1" },
                new ControlFinance.API.Models.Category { UserId = UserId, Name = "Lazer",        Color = "#96CEB4" },
                new ControlFinance.API.Models.Category { UserId = UserId, Name = "Saúde",        Color = "#FFEAA7" }
            );
            await db.SaveChangesAsync();
        }

        var cats = db.Categories.Where(c => c.UserId == UserId).ToList();
        var now  = DateTime.UtcNow;

        db.Transactions.AddRange(
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "Salário",           Type = ControlFinance.API.Models.TransactionType.Income,  Status = ControlFinance.API.Models.TransactionStatus.Paid,    Amount = 5000m,   DueDate = now.AddDays(-20), PaidAt = now.AddDays(-20), CategoryId = cats.FirstOrDefault(c => c.Name == "Salário")?.Id },
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "Supermercado",      Type = ControlFinance.API.Models.TransactionType.Expense, Status = ControlFinance.API.Models.TransactionStatus.Paid,    Amount = 387.50m, DueDate = now.AddDays(-15), PaidAt = now.AddDays(-15), CategoryId = cats.FirstOrDefault(c => c.Name == "Alimentação")?.Id },
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "Uber",              Type = ControlFinance.API.Models.TransactionType.Expense, Status = ControlFinance.API.Models.TransactionStatus.Paid,    Amount = 124m,    DueDate = now.AddDays(-10), PaidAt = now.AddDays(-10), CategoryId = cats.FirstOrDefault(c => c.Name == "Transporte")?.Id },
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "iFood",             Type = ControlFinance.API.Models.TransactionType.Expense, Status = ControlFinance.API.Models.TransactionStatus.Paid,    Amount = 89.90m,  DueDate = now.AddDays(-5),  PaidAt = now.AddDays(-5),  CategoryId = cats.FirstOrDefault(c => c.Name == "Alimentação")?.Id },
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "Netflix",           Type = ControlFinance.API.Models.TransactionType.Expense, Status = ControlFinance.API.Models.TransactionStatus.Pending, Amount = 55.90m,  DueDate = now.AddDays(5) },
            new ControlFinance.API.Models.Transaction { UserId = UserId, BankAccountId = account.Id, Name = "Aluguel",           Type = ControlFinance.API.Models.TransactionType.Expense, Status = ControlFinance.API.Models.TransactionStatus.Pending, Amount = 1200m,   DueDate = now.AddDays(10) }
        );
        await db.SaveChangesAsync();

        return Ok(new { accountId = account.Id, accountName = account.Name, balance = account.Balance });
    }

    // GET /api/polp/integrations
    [HttpGet("integrations")]
    public IActionResult GetIntegrations()
    {
        var accounts = db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new { b.Id, b.Name, b.Balance })
            .ToList();
        return Ok(accounts);
    }

    // GET /api/polp/integrations/{id}
    [HttpGet("integrations/{id}")]
    public IActionResult GetIntegrationStatus(string id)
    {
        // Mock: sempre retorna active para o sync screen avançar
        return Ok(new { status = "active" });
    }

    // POST /api/polp/integrations/{id}/sync
    [HttpPost("integrations/{id}/sync")]
    public IActionResult Sync(string id)
    {
        return Ok(new { status = "syncing" });
    }
}

public record PolpConnectDto(string ConnectorId);
