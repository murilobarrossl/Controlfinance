using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class PayeeAccountKeyTests
{
    [Fact]
    public void Conta_completa_exige_todas_as_partes_e_aceita_zeros_a_esquerda()
    {
        var company = Guid.NewGuid();
        Assert.Equal(PayeeKeys.AccountLookupInput(company, "341", "0123", "45678-9"),
            PayeeKeys.AccountLookupInput(company, "0341", "123", "0456789"));
        Assert.NotEqual(PayeeKeys.AccountLookupInput(company, "341", "0123", "45678-9"),
            PayeeKeys.AccountLookupInput(company, "341", "0124", "45678-9"));
        Assert.Null(PayeeKeys.AccountLookupInput(company, "341", null, "45678-9"));
        Assert.Null(PayeeKeys.AccountLookupInput(company, null, "0123", "45678-9"));
        Assert.NotEqual(PayeeKeys.AccountLookupInput(company, "341", "0123", "45678-9"),
            PayeeKeys.AccountLookupInput(Guid.NewGuid(), "341", "0123", "45678-9"));
    }
}

public class EmployeePaymentMatcherTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static HashSet<string> Set(params string[] values) => values.ToHashSet();

    // Funcionário com o próprio CPF e uma conta TED antiga (sem titular declarado).
    private static EmployeeIdentity Employee(Guid id, string cpfHash) =>
        new(id, [new PayeeHolder(Set(cpfHash), null)], [new PayeeAccount(Set("h-conta-" + id), null)]);

    [Fact]
    public void Hash_do_CPF_identifica_e_CNPJ_do_enriquecimento_nunca_prova()
    {
        var employees = new[] { Employee(A, "h-ana"), Employee(B, "h-bia") };
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified), SupplierPaymentMatcher.MatchEmployee(new(null, "h-ana"), employees));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, "h-outra", "h-conta-" + A), employees));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new("11222333000181", null, "h-conta-" + A), employees));
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new("11222333000181", "h-ana"), employees));
    }

    [Fact]
    public void Titular_declarado_de_Pix_identifica_mesmo_diferente_do_funcionario()
    {
        var ana = new EmployeeIdentity(A, [new PayeeHolder(Set("h-ana"), null), new PayeeHolder(Set("h-mae-da-ana"), null)], []);
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new(null, "h-mae-da-ana", "h-qualquer-conta"), [ana]));
    }

    [Fact]
    public void Titular_CNPJ_so_vale_com_CNPJ_vindo_do_banco()
    {
        var ana = new EmployeeIdentity(A, [new PayeeHolder(Set("h-ana"), null), new PayeeHolder(null, "11222333000181")], []);
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new("11222333000181", null, null, null, "11222333000181"), [ana]));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new("11222333000181", null), [ana]));
    }

    [Fact]
    public void Conta_do_banco_diferente_da_conta_TED_do_titular_nao_confirma()
    {
        var ana = new EmployeeIdentity(A,
            [new PayeeHolder(Set("h-ana"), null), new PayeeHolder(Set("h-irmao"), null, Set("h-conta-irmao"))],
            [new PayeeAccount(Set("h-conta-irmao"), "CPF")]);
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new(null, "h-irmao", "h-conta-irmao"), [ana]));
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new(null, "h-irmao"), [ana]));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, "h-irmao", "h-outra-conta"), [ana]));
    }

    [Fact]
    public void Mesmo_titular_em_dois_funcionarios_e_ambiguo()
    {
        var employees = new[]
        {
            new EmployeeIdentity(A, [new PayeeHolder(Set("h-a"), null), new PayeeHolder(Set("h-comum"), null)], []),
            new EmployeeIdentity(B, [new PayeeHolder(Set("h-b"), null), new PayeeHolder(Set("h-comum"), null)], []),
        };
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, "h-comum"), employees));
    }

    [Fact]
    public void Conta_completa_sem_documento_identifica_com_tipo_de_pessoa_compativel()
    {
        var employees = new[] { Employee(A, "h-a"), Employee(B, "h-b") };
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-conta-" + A), employees));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-conta-" + A, "PESSOA_JURIDICA"), employees));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-desconhecida"), employees));

        var mei = new EmployeeIdentity(A, [new PayeeHolder(Set("h-a"), null)], [new PayeeAccount(Set("h-conta-mei"), "CNPJ")]);
        Assert.Equal(new PayeeMatch(A, PayeePaymentLevel.Identified),
            SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-conta-mei", "PESSOA_JURIDICA"), [mei]));
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-conta-mei", "PESSOA_NATURAL"), [mei]));
    }

    [Fact]
    public void Conta_cadastrada_em_dois_funcionarios_e_ambigua()
    {
        var employees = new[]
        {
            new EmployeeIdentity(A, [new PayeeHolder(Set("h-a"), null)], [new PayeeAccount(Set("h-conjunta"), null)]),
            new EmployeeIdentity(B, [new PayeeHolder(Set("h-b"), null)], [new PayeeAccount(Set("h-conjunta"), null)]),
        };
        Assert.Null(SupplierPaymentMatcher.MatchEmployee(new(null, null, "h-conjunta"), employees));
    }
}
