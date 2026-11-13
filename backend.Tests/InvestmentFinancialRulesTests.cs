using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services.Investments;

namespace ControlFinance.API.Tests;

// Synthetic financial regressions. No provider, credentials or production records.
public class InvestmentFinancialRulesTests
{
    [Fact]
    public void AdditionAndSubtractionAreExactBeyondSystemDecimalWithoutIntermediateRounding()
    {
        var a = ExactInvestmentDecimal.Parse("79228162514264337593543950335.123456789000");
        var b = ExactInvestmentDecimal.Parse("0.000000000001");
        Assert.Equal("79228162514264337593543950335.123456789001", a.Add(b).Original);
        Assert.Equal("0.000000000001", a.Add(b).Subtract(a).Original);
    }

    [Theory]
    [InlineData("funds", "AMORTIZACAO", "amortization_not_pure_income")]
    [InlineData("funds", "COME_COTAS", "quota_tax_event")]
    [InlineData("bank-fixed-incomes", "VENCIMENTO", "maturity_not_pure_income")]
    [InlineData("treasure-titles", "COMPRA", "purchase")]
    [InlineData("variable-incomes", "JCP", "interest_on_equity")]
    [InlineData("credit-fixed-incomes", "COMPRA", "purchase")]
    [InlineData("credit-fixed-incomes", "APLICACAO", "unclassified")]
    public void ModalitiesDoNotShareAnInventedCashFlowRule(string family, string type, string expected) =>
        Assert.Equal(expected, InvestmentFinancialRules.Classify(family, type));

    [Fact]
    public void ConflictingUnversionedAmountsRemainUnavailableRatherThanReplacingKnownMoney()
    {
        var incoming = PolpInvestmentContracts.Select(JsonSerializer.Deserialize<JsonElement>("""{"gross_amount":{"amount":"200.00","currency":"BRL"}}"""),
            PolpInvestmentContracts.Family("bank-fixed-incomes").Position);
        var merged = PolpInvestmentContracts.Merge("""{"gross_amount":{"amount":"100.00","currency":"BRL"}}""", "{}", incoming, DateTime.UnixEpoch, null, replaceKnown: false);
        using var values = JsonDocument.Parse(merged.Values); using var availability = JsonDocument.Parse(merged.Availability);
        Assert.Equal("100.00", values.RootElement.GetProperty("gross_amount").GetProperty("amount").GetString());
        Assert.Null(InvestmentFinancialRules.Money(values.RootElement, availability.RootElement, "gross_amount"));
    }

    [Fact]
    public void ReportedOperationSumsNeverMixCurrenciesOrDiscardPrecision()
    {
        var rows = new[] { Movement("1.230000000001", "BRL"), Movement("2.000000000001", "BRL"), Movement("8.50", "USD") };
        var sum = InvestmentFinancialRules.SumReported(rows, "income_tax");
        var money = Assert.IsType<InvestmentMoney[]>(sum.Value);
        Assert.Equal("3.230000000002", money.Single(x => x.Currency == "BRL").Amount);
        Assert.Equal("8.50", money.Single(x => x.Currency == "USD").Amount);
    }

    [Fact]
    public void AProvisionOrMissingOperationTaxCannotBecomeEffectiveTaxZero()
    {
        var row = new InvestmentMovement { ValuesJson = """{"income_tax_provision":{"amount":"10.00","currency":"BRL"}}""", AvailabilityJson = "{}" };
        var sum = InvestmentFinancialRules.SumReported(new[] { row }, "income_tax");
        Assert.Equal("unavailable", sum.Status); Assert.Null(sum.Value);
    }

    [Theory]
    [InlineData("CDI")]
    [InlineData("SELIC")]
    [InlineData("IPCA")]
    [InlineData("PRE_FIXADO")]
    public void ConflictingOfficialUnitsCannotBeNormalizedByMagnitudeOrIndexer(string indexer)
    {
        var terms = new InvestmentContract { ValuesJson = JsonSerializer.Serialize(new { remuneration = new { values = new { indexer, pre_fixed_rate = "0.15" } } }) };
        var result = JsonSerializer.SerializeToElement(InvestmentFinancialRules.Projection(new ImportedInvestment { Family = "bank-fixed-incomes" }, terms));
        Assert.Equal("unavailable", result.GetProperty("status").GetString());
        Assert.Equal("ambiguous_documented_rate_scale", result.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("series").ValueKind);
    }

    private static InvestmentMovement Movement(string amount, string currency) => new()
    {
        ValuesJson = JsonSerializer.Serialize(new { income_tax = new { amount, currency } }),
        AvailabilityJson = """{"income_tax":"available"}"""
    };
}
