using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ControlFinance.API.Tests.Support;

// Segundo usuário no mesmo banco, com uma conexão Polp completa e dados manuais. Os testes de
// isolamento chamam os endpoints como t.User e verificam que nada disso é lido ou alterado.
public sealed record OtherUser(
    User User,
    PolpIntegration Integration,
    BankAccount Account,
    BankAccount Card,
    BankAccount ManualAccount,
    Category Category,
    Transaction Transaction)
{
    public const string AccountName = "Conta da outra pessoa";
    public const string TransactionName = "Compra da outra pessoa";
    public const decimal Balance = 98765.43m;

    public static OtherUser Add(TestDb t)
    {
        var user = new User
        {
            Name = "Outra Pessoa", Email = $"{Guid.NewGuid():N}@teste.com",
            DocumentHash = Guid.NewGuid().ToString("N"), PasswordHash = "x"
        };
        t.Db.Users.Add(user);
        t.Db.SaveChanges();

        var integration = new PolpIntegration
        {
            UserId = user.Id, PolpIntegrationId = Guid.NewGuid(), InstitutionId = Guid.NewGuid(),
            InstitutionName = "Banco Alheio", Status = "AUTHORISED", SyncedLocally = true
        };
        t.Db.PolpIntegrations.Add(integration);

        BankAccount Account(string name, string? type, Guid? consentId) => new()
        {
            UserId = user.Id, Name = name, Balance = Balance, PolpAccountType = type,
            PolpAccountId = consentId is null ? null : Guid.NewGuid(), PolpIntegrationId = consentId
        };
        var account = Account(AccountName, "CONTA_DEPOSITO_A_VISTA", integration.PolpIntegrationId);
        var card = Account("Cartão da outra pessoa", CreditCardAccountDetector.CardType, integration.PolpIntegrationId);
        var manual = Account("Conta manual da outra pessoa", null, null);
        t.Db.BankAccounts.AddRange(account, card, manual);

        var category = new Category { UserId = user.Id, Name = "Categoria da outra pessoa" };
        t.Db.Categories.Add(category);
        t.Db.SaveChanges();

        var transaction = new Transaction
        {
            UserId = user.Id, BankAccountId = account.Id, CategoryId = category.Id, Name = TransactionName,
            Type = TransactionType.Expense, Status = TransactionStatus.Pending, Amount = 321.09m,
            DueDate = Dates.Today.AddDays(1), Description = Guid.NewGuid().ToString()
        };
        t.Db.Transactions.Add(transaction);
        t.Db.SaveChanges();

        return new OtherUser(user, integration, account, card, manual, category, transaction);
    }
}

public static class Http
{
    public static int? Status(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode ?? 200;

    public static string Json(IActionResult result) =>
        result is ObjectResult o ? JsonSerializer.Serialize(o.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) : "";
}
