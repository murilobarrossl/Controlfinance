using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ControlFinance.API.Services.Investments;

// Arbitrary precision base-10 coefficient/scale. Keeps the original lexical scale, including
// trailing zeros. No double/float and no decimal.TryParse rounding on long unit prices/rates.
public sealed record ExactInvestmentDecimal(BigInteger Coefficient, int Scale, string Original)
{
    private static readonly Regex Syntax = new(@"\A[+-]?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant);
    public static ExactInvestmentDecimal Zero { get; } = Parse("0");
    public static ExactInvestmentDecimal Parse(string value)
    {
        if (value.Length > 256 || !Syntax.IsMatch(value))
            throw new PolpPermanentException("Decimal de investimento fora do contrato.");
        var dot = value.IndexOf('.');
        var scale = dot < 0 ? 0 : value.Length - dot - 1;
        return new(BigInteger.Parse(value.Replace(".", ""), CultureInfo.InvariantCulture), scale, value);
    }

    public ExactInvestmentDecimal Add(ExactInvestmentDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return From(Coefficient * BigInteger.Pow(10, scale - Scale)
            + other.Coefficient * BigInteger.Pow(10, scale - other.Scale), scale);
    }
    public ExactInvestmentDecimal Subtract(ExactInvestmentDecimal other) => Add(From(-other.Coefficient, other.Scale));
    public static ExactInvestmentDecimal From(BigInteger coefficient, int scale)
    {
        if (scale is < 0 or > 256) throw new ArgumentOutOfRangeException(nameof(scale));
        var digits = BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var text = (coefficient.Sign < 0 ? "-" : "") + (scale == 0 ? digits : digits.Insert(digits.Length - scale, "."));
        return new(coefficient, scale, text);
    }
}
