using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using ControlFinance.API.Controllers;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlFinance.API.Tests.Support;

public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly DbContextOptions<AppDbContext> _options;

    public IEncryptionService Encryption { get; }
    public AppDbContext Db { get; }
    public User User { get; }

    public TestDb(string userName = "Maria Silva")
    {
        Encryption = TestEncryption.Create();
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        Db = NewContext();
        Db.Database.EnsureCreated();

        User = new User
        {
            Name = userName,
            Email = $"{Guid.NewGuid():N}@teste.com",
            DocumentHash = Guid.NewGuid().ToString("N"),
            PasswordHash = "x"
        };
        Db.Users.Add(User);
        Db.SaveChanges();

        _services = new ServiceCollection()
            .AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()))
            .AddSingleton(Db)
            .AddSingleton<SessionValidator>()
            .BuildServiceProvider();
    }

    public AppDbContext NewContext() => new(_options, Encryption);

    public T Controller<T>(Func<AppDbContext, T> create) where T : ApiControllerBase
    {
        var controller = create(Db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, User.Id.ToString())], "teste"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal, RequestServices = _services }
        };
        return controller;
    }

    public BankAccount AddAccount(string name = "Conta", decimal balance = 0, bool active = true,
        string? type = null, decimal? creditLimit = null, decimal? usedLimit = null, Guid? polpAccountId = null)
    {
        var account = new BankAccount
        {
            UserId = User.Id, Name = name, Balance = balance, IsActive = active,
            PolpAccountType = type, CreditLimit = creditLimit, UsedLimit = usedLimit, PolpAccountId = polpAccountId
        };
        Db.BankAccounts.Add(account);
        Db.SaveChanges();
        return account;
    }

    public Category AddCategory(string name)
    {
        var category = new Category { UserId = User.Id, Name = name };
        Db.Categories.Add(category);
        Db.SaveChanges();
        return category;
    }

    public Transaction AddTransaction(BankAccount? account, TransactionType type, decimal amount, DateTime dueDate,
        string name = "Lancamento", TransactionStatus status = TransactionStatus.Paid, Category? category = null)
    {
        var t = new Transaction
        {
            UserId = User.Id, BankAccountId = account?.Id, CategoryId = category?.Id,
            Name = name, Type = type, Status = status, Amount = amount,
            DueDate = DateTime.SpecifyKind(dueDate, DateTimeKind.Utc)
        };
        Db.Transactions.Add(t);
        Db.SaveChanges();
        return t;
    }

    public CreditCard AddCard(decimal limit, decimal used, int closingDay = 5, int dueDay = 15)
    {
        var card = new CreditCard
        {
            UserId = User.Id, Name = "Cartao", CreditLimit = limit, UsedLimit = used,
            ClosingDay = closingDay, DueDay = dueDay
        };
        Db.CreditCards.Add(card);
        Db.SaveChanges();
        return card;
    }

    public Installment AddInstallment(CreditCard? card, BankAccount? account, decimal installmentAmount,
        int current, int total, DateTime nextDueDate, decimal? totalAmount = null, int? dueDay = null)
    {
        var i = new Installment
        {
            UserId = User.Id, CreditCardId = card?.Id, BankAccountId = account?.Id,
            Description = "Compra", TotalAmount = totalAmount ?? installmentAmount * total,
            TotalInstallments = total, CurrentInstallment = current, InstallmentAmount = installmentAmount, DueDay = dueDay,
            NextDueDate = DateTime.SpecifyKind(nextDueDate, DateTimeKind.Utc)
        };
        Db.Installments.Add(i);
        Db.SaveChanges();
        return i;
    }

    public void Dispose()
    {
        _services.Dispose();
        Db.Dispose();
        _connection.Dispose();
    }
}

public static class TestEncryption
{
    public static IEncryptionService Create(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
        };
        if (extra is not null)
            foreach (var (k, v) in extra) settings[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new EncryptionService(config);
    }

    public static string RandomSecret() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}

public static class Dates
{
    public static DateTime Today => DateTime.SpecifyKind(BrazilNow().Date, DateTimeKind.Utc);

    public static DateTime UtcToday => DateTime.UtcNow.Date;

    public static DateTime Day15MonthsAgo(int monthsElapsed)
    {
        var today = Today;
        var thisMonth15 = new DateTime(today.Year, today.Month, 15, 0, 0, 0, DateTimeKind.Utc);
        var lastPast15 = thisMonth15 < today ? thisMonth15 : thisMonth15.AddMonths(-1);
        return lastPast15.AddMonths(-(monthsElapsed - 1));
    }

    public static DateTime BrazilNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
}
