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

    [Fact]
    public void ConflictingUnversionedAmountsRemainUnavailableRatherThanReplacingKnownMoney()
    {
        var incoming = PolpInvestmentContracts.Select(JsonSerializer.Deserialize<JsonElement>("""{"gross_amount":{"amount":"200.00","currency":"BRL"}}"""),
            PolpInvestmentContracts.Family("bank-fixed-incomes").Position);
        var merged = PolpInvestmentContracts.Merge("""{"gross_amount":{"amount":"100.00","currency":"BRL"}}""", "{}", incoming, DateTime.UnixEpoch, null, replaceKnown: false);
        using var values = JsonDocument.Parse(merged.Values); using var availability = JsonDocument.Parse(merged.Availability);
        Assert.Equal("100.00", values.RootElement.GetProperty("gross_amount").GetProperty("amount").GetString());
        Assert.NotEqual("available", availability.RootElement.GetProperty("gross_amount").GetString());
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
}
