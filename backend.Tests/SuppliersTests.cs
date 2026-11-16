using System.Text;
using ControlFinance.API.Controllers;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Results = ControlFinance.API.Tests.Support.Results;

namespace ControlFinance.API.Tests;

public class SuppliersTests
{
    private const string Cnpj = "11222333000181";
    private const string Cpf = "52998224725";
    private const string OtherCnpj = "45997418000153";

    private static TestDb Company()
    {
        var t = new TestDb("Padaria Central Ltda");
        t.User.DocumentType = DocumentTypes.Cnpj;
        t.Db.SaveChanges();
        return t;
    }

    private static SuppliersController Controller(TestDb t) => t.Controller(db => new SuppliersController(db, t.Encryption));

    private static SupplierUpsertDto Dto(string name = "Moinho Paulista Ltda", string? document = Cnpj, string? tradeName = "Moinho SP", string? category = "Insumos") =>
        new(name, document, tradeName, category);

    private static async Task<SupplierDetailDto> Create(TestDb t, SupplierUpsertDto dto) =>
        Assert.IsType<SupplierDetailDto>(Assert.IsType<CreatedAtActionResult>(await Controller(t).Create(dto, default)).Value);

    private static string Message(IActionResult result) => (string)((dynamic)((ObjectResult)result).Value!).message;

    [Fact]
    public void Rotas_de_fornecedores_exigem_login_CNPJ() =>
        Assert.Contains(typeof(SuppliersController).GetCustomAttributes(true), a => a is BusinessHolderOnlyAttribute);

    [Fact]
    public async Task Cadastro_aceita_CNPJ_ou_CPF_e_so_devolve_documento_mascarado()
    {
        using var t = Company();

        var company = await Create(t, Dto());
        var person = await Create(t, Dto(name: "João da Silva", document: Cpf, tradeName: null, category: null));

        Assert.Equal("CNPJ", company.DocumentType);
        Assert.Equal("••.•••.•••/0001-81", company.DocumentMasked);
        Assert.Equal("CPF", person.DocumentType);
        Assert.Equal("•••.•••.247-25", person.DocumentMasked);
        Assert.Null(person.TradeName);
        Assert.Null(person.Pix);
        Assert.DoesNotContain(Cnpj, Http.Json(new OkObjectResult(company)));

        await using var connection = t.Db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT group_concat(\"LegalNameEncrypted\" || '|' || \"DocumentEncrypted\" || '|' || coalesce(\"TradeNameEncrypted\", '')) FROM \"Suppliers\"";
        var raw = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.DoesNotContain("Moinho", raw);
        Assert.DoesNotContain(Cnpj, raw);
        Assert.DoesNotContain(Cpf, raw);
    }

    [Theory]
    [InlineData("11111111111111", "CPF ou CNPJ inválido")]
    [InlineData("123", "CPF ou CNPJ inválido")]
    [InlineData(null, "CPF ou CNPJ inválido")]
    public async Task Documento_invalido_e_recusado(string? document, string expected)
    {
        using var t = Company();
        var result = await Controller(t).Create(Dto(document: document), default);
        Assert.Equal(400, Http.Status(result));
        Assert.Contains(expected, Message(result));
        Assert.Empty(t.Db.Suppliers);
    }

    [Fact]
    public async Task Mesmo_documento_ou_chave_Pix_na_empresa_e_recusado_e_o_hash_nao_cruza_com_funcionarios()
    {
        using var t = Company();
        var first = await Create(t, Dto());
        await Controller(t).SetPix(first.Id, new PayeePixDto("Cnpj", Cnpj), default);
        var second = await Create(t, Dto(name: "Laticínios Sul", document: OtherCnpj));

        Assert.Equal(400, Http.Status(await Controller(t).Create(Dto(name: "Outro nome"), default)));
        var samePix = await Controller(t).SetPix(second.Id, new PayeePixDto("Cnpj", "11.222.333/0001-81"), default);
        Assert.Equal(400, Http.Status(samePix));
        Assert.Contains("outro fornecedor", Message(samePix));

        var hash = t.Db.Suppliers.Single(s => s.Id == first.Id).DocumentHash;
        Assert.NotEqual(t.Encryption.ComputeLookupHash(EmployeeKeys.CpfLookupInput(t.User.Id, Cnpj)), hash);
        Assert.NotEqual(t.Encryption.ComputeLookupHash(Cnpj), hash);
    }

    [Fact]
    public async Task Edicao_mantem_o_documento_quando_vazio_e_pagamento_pode_ser_trocado_e_removido()
    {
        using var t = Company();
        var created = await Create(t, Dto());

        var edited = Results.OkValue<SupplierDetailDto>(await Controller(t).Update(created.Id, Dto(name: "Moinho Paulista S.A.", document: "", tradeName: "", category: "Matéria-prima"), default));
        Assert.Equal("Moinho Paulista S.A.", edited.LegalName);
        Assert.Equal(created.DocumentMasked, edited.DocumentMasked);
        Assert.Null(edited.TradeName);
        Assert.Equal("Matéria-prima", edited.Category);

        Assert.Equal(400, Http.Status(await Controller(t).SetBankAccount(created.Id, new PayeeBankDto("34", "1", "1", "Corrente"), default)));
        var withBank = Results.OkValue<SupplierDetailDto>(await Controller(t).SetBankAccount(created.Id, new PayeeBankDto("237", "0123", "45678-9", "Corrente"), default));
        Assert.Equal("•••• 6789", withBank.Bank!.AccountMasked);
        Assert.Null(Results.OkValue<SupplierDetailDto>(await Controller(t).RemoveBankAccount(created.Id, default)).Bank);
    }

    [Fact]
    public async Task Excluir_apaga_o_fornecedor_e_libera_o_documento_e_o_resumo_conta_so_o_que_existe()
    {
        using var t = Company();
        var first = await Create(t, Dto());
        await Controller(t).SetPix(first.Id, new PayeePixDto("Cnpj", Cnpj), default);
        await Create(t, Dto(name: "Laticínios Sul", document: OtherCnpj));

        var summary = Results.OkValue<SupplierSummaryDto>(await Controller(t).Summary(default));
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.WithPaymentCount);

        Assert.Equal(204, Http.Status(await Controller(t).Delete(first.Id, default)));
        Assert.Equal(404, Http.Status(await Controller(t).Get(first.Id, default)));
        Assert.Equal(404, Http.Status(await Controller(t).Delete(first.Id, default)));
        Assert.Equal(1, Results.OkValue<SupplierSummaryDto>(await Controller(t).Summary(default)).TotalCount);

        var again = await Create(t, Dto());
        Assert.Equal(200, Http.Status(await Controller(t).SetPix(again.Id, new PayeePixDto("Cnpj", Cnpj), default)));
    }

    [Fact]
    public async Task Fornecedor_de_outra_empresa_nao_e_lido_nem_alterado()
    {
        using var t = Company();
        var stranger = new User { Name = "Outra Ltda", Email = "o@o.com", DocumentHash = "o", DocumentType = DocumentTypes.Cnpj, PasswordHash = "x" };
        t.Db.Users.Add(stranger);
        var foreign = new Supplier { UserId = stranger.Id, LegalName = "Fornecedor Alheio", Document = Cnpj, DocumentType = "CNPJ", DocumentHash = "x" };
        t.Db.Suppliers.Add(foreign);
        t.Db.SaveChanges();
        var c = Controller(t);

        Assert.Equal(404, Http.Status(await c.Get(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.Update(foreign.Id, Dto(), default)));
        Assert.Equal(404, Http.Status(await c.Delete(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.SetPix(foreign.Id, new PayeePixDto("Email", "a@b.com"), default)));
        Assert.Equal(404, Http.Status(await c.RemovePix(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.SetBankAccount(foreign.Id, new PayeeBankDto("341", "1", "1", "Corrente"), default)));
        Assert.Equal(404, Http.Status(await c.RemoveBankAccount(foreign.Id, default)));
        Assert.Empty(Results.OkValue<SupplierListDto>(await c.List(null)).Items);
        Assert.Equal(0, Results.OkValue<SupplierSummaryDto>(await c.Summary(default)).TotalCount);

        await Create(t, Dto());
        Assert.Equal("Fornecedor Alheio", t.NewContext().Suppliers.Single(s => s.Id == foreign.Id).LegalName);
    }

    [Fact]
    public async Task Lista_pagina_no_servidor_e_busca_por_razao_social_ou_nome_fantasia()
    {
        using var t = Company();
        for (var i = 0; i < 27; i++)
        {
            var cnpj = ValidCnpj(i);
            await Create(t, Dto(name: $"Fornecedor {i:D2}", document: cnpj, tradeName: null));
        }
        var moinho = await Create(t, Dto(name: "Moinho Paulista Ltda", document: Cnpj, tradeName: "Pão de Açúcar Farinhas"));

        var first = Results.OkValue<SupplierListDto>(await Controller(t).List(null, 1, 25));
        var second = Results.OkValue<SupplierListDto>(await Controller(t).List(null, 2, 25));
        Assert.Equal(28, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(28, first.Items.Concat(second.Items).Select(i => i.Id).Distinct().Count());

        Assert.Equal(moinho.Id, Assert.Single(Results.OkValue<SupplierListDto>(await Controller(t).List("pao de acucar")).Items).Id);
        Assert.Equal(moinho.Id, Assert.Single(Results.OkValue<SupplierListDto>(await Controller(t).List("moinho")).Items).Id);
        Assert.Equal(100, Results.OkValue<SupplierListDto>(await Controller(t).List(null, 1, 10_000)).PageSize);
    }

    [Fact]
    public async Task Exportacao_traz_os_fornecedores_e_a_exclusao_da_conta_apaga()
    {
        using var t = Company();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte");
        t.Db.SaveChanges();
        await Create(t, Dto());
        var account = t.Controller(db => new AccountController(
            db,
            new PolpDisconnectService(db, new NullPolpService(), new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance),
            new SessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            new RateLimitService(),
            new ConfigurationBuilder().Build()));
        var body = new MemoryStream();
        account.ControllerContext.HttpContext.Response.Body = body;

        await account.Export(default);
        await account.ControllerContext.HttpContext.Response.BodyWriter.FlushAsync();
        var json = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("fornecedores", json);
        Assert.Contains("Moinho Paulista Ltda", json);

        await t.NewContext().Users.Where(u => u.Id == t.User.Id).ExecuteDeleteAsync();
        Assert.Empty(t.NewContext().Suppliers);
    }

    private static string ValidCnpj(int seed)
    {
        var digits = $"{10000000 + seed * 7919:D8}0001".Select(c => c - '0').ToList();
        foreach (var weights in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var r = digits.Select((d, i) => d * weights[i]).Sum() % 11;
            digits.Add(r < 2 ? 0 : 11 - r);
        }
        return string.Concat(digits);
    }
}
