using System.Numerics;
using System.Text.Json;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;

namespace ControlFinance.API.Tests;

// Synthetic contract fixtures only; these tests do not call Polp or any database.
public class InvestmentImportContractTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static readonly DateTime Observed = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UnitPriceBeyondSystemDecimalPreservesEveryDigitAndScale()
    {
        const string original = "79228162514264337593543950335.123456789000";
        var value = ExactInvestmentDecimal.Parse(original);
        Assert.Equal(original, value.Original);
        Assert.Equal(12, value.Scale);
        Assert.Equal(BigInteger.Parse("79228162514264337593543950335123456789000"), value.Coefficient);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e-10")]
    [InlineData("1,50")]
    public void NonContractDecimalFormatsAreRejectedInsteadOfRounded(string text) =>
        Assert.Throws<PolpPermanentException>(() => ExactInvestmentDecimal.Parse(text));

    [Fact]
    public void NullPositionDoesNotTurnPreviousGrossAmountIntoZero()
    {
        var schema = PolpInvestmentContracts.Family("bank-fixed-incomes").Position;
        var document = PolpInvestmentContracts.Select(Json("""{"gross_amount":null}"""), schema);
        var merged = PolpInvestmentContracts.Merge("""{"gross_amount":{"amount":"1234.567800","currency":"BRL"}}""", "{}", document, Observed, null);
        var current = Json(merged.Values);
        Assert.Equal("1234.567800", current.GetProperty("gross_amount").GetProperty("amount").GetString());
        Assert.Equal("not_informed", Json(merged.Availability).GetProperty("gross_amount").GetString());
        Assert.Equal("not_provided", Json(merged.Availability).GetProperty("net_amount").GetString());
    }

    [Fact]
    public void MissingCurrencyIsObservedAsIncompleteWithoutCombiningItWithAnOldCurrency()
    {
        var schema = PolpInvestmentContracts.Family("bank-fixed-incomes").Position;
        var incoming = PolpInvestmentContracts.Select(Json("""{"gross_amount":{"amount":"77.0000"}}"""), schema);
        Assert.Equal("77.0000", incoming.Values["gross_amount"]!["amount"]!.GetValue<string>());
        var merged = PolpInvestmentContracts.Merge("""{"gross_amount":{"amount":"10.0000","currency":"USD"}}""", "{}", incoming, Observed, null);
        var current = Json(merged.Values).GetProperty("gross_amount");
        Assert.Equal("10.0000", current.GetProperty("amount").GetString());
        Assert.Equal("USD", current.GetProperty("currency").GetString());
        Assert.Equal("incomplete", Json(merged.Availability).GetProperty("gross_amount").GetString());
    }

    [Fact]
    public void FundTaxProvisionIsNotImportedAsTaxWithheld()
    {
        var position = PolpInvestmentContracts.Select(Json("""{"income_tax_provision":{"amount":"12.3400","currency":"BRL"}}"""),
            PolpInvestmentContracts.Family("funds").Position);
        Assert.Equal("12.3400", position.Values["income_tax_provision"]!["amount"]!.GetValue<string>());
        Assert.False(position.Values.ContainsKey("income_tax"));
        Assert.False(position.Values.ContainsKey("income_net"));
    }

    [Fact]
    public void ReservedBalancesKeepCurrenciesAndDecimalScalesSeparate()
    {
        var reserve = PolpInvestmentContracts.Select(Json("""{"available_amount":[{"amount":"1.2300","currency":"BRL"},{"amount":"2.000001","currency":"USD"}]}"""),
            PolpInvestmentContracts.ReservedFields);
        var amounts = reserve.Values["available_amount"]!.AsArray();
        Assert.Equal(2, amounts.Count);
        Assert.Equal("1.2300", amounts[0]!["amount"]!.GetValue<string>());
        Assert.Equal("BRL", amounts[0]!["currency"]!.GetValue<string>());
        Assert.Equal("2.000001", amounts[1]!["amount"]!.GetValue<string>());
        Assert.Equal("USD", amounts[1]!["currency"]!.GetValue<string>());
        Assert.Equal("not_provided", amounts[1]!["remuneration_availability"]!.GetValue<string>());
    }

    [Fact]
    public void ContractFactorsAreNotGuessedOrConvertedToPercentages()
    {
        var contract = PolpInvestmentContracts.Select(Json("""{"remuneration":{"indexer":"CDI","pre_fixed_rate":"0.050000","post_fixed_indexer_percentage":"1.000000","rate_periodicity":"ANUAL"}}"""),
            PolpInvestmentContracts.Family("bank-fixed-incomes").Contract);
        var terms = contract.Values["remuneration"]!["values"]!;
        Assert.Equal("0.050000", terms["pre_fixed_rate"]!.GetValue<string>());
        Assert.Equal("1.000000", terms["post_fixed_indexer_percentage"]!.GetValue<string>());
    }

    [Fact]
    public void MissingReferenceDoesNotBecomeTodayOrPurchaseDate()
    {
        var position = PolpInvestmentContracts.Select(Json("""{"gross_amount":{"amount":"1.00","currency":"BRL"}}"""),
            PolpInvestmentContracts.Family("treasure-titles").Position);
        Assert.Null(PolpInvestmentContracts.Reference(position));
        Assert.False(position.Values.ContainsKey("reference_date_time"));
    }
}
