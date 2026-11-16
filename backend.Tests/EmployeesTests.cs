using ControlFinance.API.Controllers;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Results = ControlFinance.API.Tests.Support.Results;

namespace ControlFinance.API.Tests;

public class EmployeesTests
{
    // CPF válido a partir de 9 dígitos quaisquer: os testes precisam de vários CPFs diferentes.
    private static string Cpf(int seed)
    {
        var d = $"{100000000 + seed * 7919 % 899999999:D9}".Select(c => c - '0').ToList();
        for (var round = 0; round < 2; round++)
        {
            var sum = 0;
            for (var i = 0; i < d.Count; i++) sum += d[i] * (d.Count + 1 - i);
            var r = sum % 11;
            d.Add(r < 2 ? 0 : 11 - r);
        }
        var cpf = string.Concat(d);
        return DocumentValidator.IsValidCpf(cpf) ? cpf : Cpf(seed + 1000);
    }

    private static TestDb Company()
    {
        var t = new TestDb("Padaria Central Ltda");
        t.User.DocumentType = DocumentTypes.Cnpj;
        t.Db.SaveChanges();
        return t;
    }

    private static EmployeesController Controller(TestDb t) => t.Controller(db => new EmployeesController(db, t.Encryption));

    private static EmployeeCoreDto Dto(string name = "Maria da Silva", string? cpf = null, decimal salary = 3500.50m) =>
        new(name, cpf ?? Cpf(1), "Padeira", salary, new DateOnly(2024, 3, 1));

    private static async Task<EmployeeDetailDto> Create(TestDb t, EmployeeCoreDto dto)
    {
        var result = await Controller(t).Create(dto, default);
        return Assert.IsType<EmployeeDetailDto>(Assert.IsType<CreatedAtActionResult>(result).Value);
    }

    private static string Message(IActionResult result) => (string)((dynamic)((ObjectResult)result).Value!).message;

    private static async Task<int> FilterStatus(TestDb t)
    {
        var context = new ActionExecutingContext(
            new ActionContext(Controller(t).HttpContext, new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?>(), null!);
        var passed = false;
        await new BusinessHolderOnlyAttribute().OnActionExecutionAsync(context, () =>
        {
            passed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });
        return passed ? 200 : ((ObjectResult)context.Result!).StatusCode!.Value;
    }

    [Theory]
    [InlineData("CNPJ", 200)]
    [InlineData("CPF", 403)]
    [InlineData(null, 403)]
    public async Task So_login_CNPJ_passa_pelo_filtro_do_backend(string? documentType, int expected)
    {
        using var t = new TestDb();
        t.User.DocumentType = documentType;
        t.Db.SaveChanges();

        Assert.Equal(expected, await FilterStatus(t));
        Assert.Contains(typeof(EmployeesController).GetCustomAttributes(true), a => a is BusinessHolderOnlyAttribute);
    }

    [Fact]
    public async Task Cadastro_rapido_so_com_os_cinco_campos_nao_presume_vinculo_departamento_nem_pagamento()
    {
        using var t = Company();

        var detail = await Create(t, Dto());

        Assert.Null(detail.EmploymentType);
        Assert.Null(detail.Department);
        Assert.Null(detail.Pix);
        Assert.Null(detail.Bank);
        Assert.Null(t.NewContext().Employees.Single().EmploymentType);
    }

    [Fact]
    public async Task Dados_pessoais_ficam_cifrados_e_a_API_devolve_so_mascaras()
    {
        using var t = Company();
        var cpf = Cpf(2);
        var created = await Create(t, Dto(cpf: cpf));
        await Controller(t).SetPix(created.Id, new PayeePixDto("Email", "Maria.Silva@Exemplo.com.br"), default);
        var detail = Results.OkValue<EmployeeDetailDto>(
            await Controller(t).SetBankAccount(created.Id, new PayeeBankDto("341", "1234", "987654-3", "Corrente"), default));

        var json = Http.Json(new OkObjectResult(detail));
        Assert.DoesNotContain(cpf, json);
        Assert.DoesNotContain("maria.silva@exemplo", json);
        Assert.DoesNotContain("987654", json);
        Assert.Equal($"•••.•••.{cpf[6..9]}-{cpf[9..]}", detail.CpfMasked);
        Assert.Equal("m•••@exemplo.com.br", detail.Pix!.KeyMasked);
        Assert.Equal("•••• 6543", detail.Bank!.AccountMasked);

        await using var connection = t.Db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT \"FullNameEncrypted\" || '|' || \"CpfEncrypted\" || '|' || \"GrossSalaryEncrypted\" || '|' || \"PixKeyEncrypted\" || '|' || \"PayeeAccountEncrypted\" FROM \"Employees\"";
        var raw = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.DoesNotContain("Maria", raw);
        Assert.DoesNotContain(cpf, raw);
        Assert.DoesNotContain("3500", raw);
        Assert.DoesNotContain("exemplo", raw);
        Assert.DoesNotContain("987654", raw);
    }

    [Fact]
    public async Task Mesmo_CPF_ou_chave_Pix_na_mesma_empresa_e_recusado_e_o_hash_nao_cruza_empresas()
    {
        using var t = Company();
        var cpf = Cpf(3);
        var first = await Create(t, Dto(cpf: cpf));
        await Controller(t).SetPix(first.Id, new PayeePixDto("Phone", "(11) 98765-4321"), default);
        var second = await Create(t, Dto(name: "Outra Pessoa", cpf: Cpf(4)));

        Assert.Equal(400, Http.Status(await Controller(t).Create(Dto(name: "Mais Alguém", cpf: cpf), default)));
        var samePix = await Controller(t).SetPix(second.Id, new PayeePixDto("Phone", "+55 11 98765-4321"), default);
        Assert.Equal(400, Http.Status(samePix));
        Assert.Contains("outro funcionário", Message(samePix));

        var other = new User { Name = "Outra Ltda", Email = "x@y.com", DocumentHash = "h", DocumentType = DocumentTypes.Cnpj, PasswordHash = "x" };
        t.Db.Users.Add(other);
        t.Db.SaveChanges();
        var hashes = t.Db.Employees.Select(e => e.CpfHash).ToList();
        Assert.DoesNotContain(t.Encryption.ComputeLookupHash(EmployeeKeys.CpfLookupInput(other.Id, cpf)), hashes);
        Assert.DoesNotContain(t.Encryption.ComputeLookupHash(cpf), hashes);
    }

    [Theory]
    [InlineData("12345678900", 3500, "CPF inválido")]
    [InlineData(null, 0, "salário")]
    [InlineData(null, 1500.555, "salário")]
    public async Task Cadastro_com_CPF_ou_salario_invalido_e_recusado(string? cpf, double salary, string expected)
    {
        using var t = Company();

        var result = await Controller(t).Create(Dto(cpf: cpf, salary: (decimal)salary), default);

        Assert.Equal(400, Http.Status(result));
        Assert.Contains(expected, Message(result));
        Assert.Empty(t.Db.Employees);
    }

    [Theory]
    [InlineData("Email", "nao-e-email", "e-mail")]
    [InlineData("Cpf", "11111111111", "CPF inválido")]
    [InlineData("Random", "abc", "aleatória")]
    [InlineData("Boleto", "123", "Tipo de chave")]
    public async Task Chave_Pix_invalida_e_recusada_sem_alterar_o_cadastro(string type, string key, string expected)
    {
        using var t = Company();
        var created = await Create(t, Dto());

        var result = await Controller(t).SetPix(created.Id, new PayeePixDto(type, key), default);

        Assert.Equal(400, Http.Status(result));
        Assert.Contains(expected, Message(result));
        Assert.Null(t.NewContext().Employees.Single().PixKeyHash);
    }

    [Fact]
    public async Task Dados_bancarios_invalidos_sao_recusados()
    {
        using var t = Company();
        var created = await Create(t, Dto());
        foreach (var bank in new[]
        {
            new PayeeBankDto("34", "1234", "1234-5", "Corrente"),
            new PayeeBankDto("341", "12345", "1234-5", "Corrente"),
            new PayeeBankDto("341", "1234", "12a4", "Corrente"),
            new PayeeBankDto("341", "1234", "1234-5", "Investimento"),
        })
            Assert.Equal(400, Http.Status(await Controller(t).SetBankAccount(created.Id, bank, default)));
        Assert.Null(t.NewContext().Employees.Single().PayeeBankCode);
    }

    [Fact]
    public async Task Edicao_principal_mantem_CPF_vinculo_e_pagamento_e_cada_metodo_pode_ser_trocado_ou_removido()
    {
        using var t = Company();
        var cpf = Cpf(5);
        var created = await Create(t, Dto(cpf: cpf));
        await Controller(t).SetClassification(created.Id, new EmployeeClassificationDto("Produção", "Clt"), default);
        await Controller(t).SetPix(created.Id, new PayeePixDto("Cpf", cpf), default);
        await Controller(t).SetBankAccount(created.Id, new PayeeBankDto("001", "0001", "12345-6", "Salario"), default);

        var edited = Results.OkValue<EmployeeDetailDto>(await Controller(t).Update(created.Id, Dto(name: "Maria Souza", cpf: "", salary: 4000m), default));
        Assert.Equal("Maria Souza", edited.FullName);
        Assert.Equal(created.CpfMasked, edited.CpfMasked);
        Assert.Equal("Clt", edited.EmploymentType);
        Assert.Equal("Produção", edited.Department);
        Assert.NotNull(edited.Pix);
        Assert.NotNull(edited.Bank);
        Assert.Equal(4000m, edited.GrossSalary);

        var changedPix = Results.OkValue<EmployeeDetailDto>(await Controller(t).SetPix(created.Id, new PayeePixDto("Email", "maria@padaria.com"), default));
        Assert.Equal("Email", changedPix.Pix!.Type);

        Assert.Null(Results.OkValue<EmployeeDetailDto>(await Controller(t).RemovePix(created.Id, default)).Pix);
        Assert.Null(Results.OkValue<EmployeeDetailDto>(await Controller(t).RemoveBankAccount(created.Id, default)).Bank);
        var stored = t.NewContext().Employees.Single();
        Assert.Null(stored.PixKeyHash);
        Assert.Null(stored.PayeeAccount);
    }

    [Fact]
    public async Task Vinculo_pode_voltar_a_nao_informado_e_valor_desconhecido_e_recusado()
    {
        using var t = Company();
        var created = await Create(t, Dto());

        Assert.Equal("Estagio", Results.OkValue<EmployeeDetailDto>(
            await Controller(t).SetClassification(created.Id, new EmployeeClassificationDto(" Vendas ", "estagio"), default)).EmploymentType);
        var cleared = Results.OkValue<EmployeeDetailDto>(await Controller(t).SetClassification(created.Id, new EmployeeClassificationDto("", null), default));
        Assert.Null(cleared.EmploymentType);
        Assert.Null(cleared.Department);
        Assert.Equal(400, Http.Status(await Controller(t).SetClassification(created.Id, new EmployeeClassificationDto(null, "Freela"), default)));
    }

    [Fact]
    public async Task Desligar_preserva_o_cadastro_e_reativar_volta_a_contar()
    {
        using var t = Company();
        var created = await Create(t, Dto());

        Assert.Equal(400, Http.Status(await Controller(t).Terminate(created.Id, new EmployeeTerminationDto(new DateOnly(2023, 1, 1)), default)));
        var terminated = Results.OkValue<EmployeeDetailDto>(await Controller(t).Terminate(created.Id, new EmployeeTerminationDto(new DateOnly(2025, 6, 30)), default));
        Assert.Equal("Terminated", terminated.Status);
        Assert.Equal(3500.50m, terminated.GrossSalary);
        Assert.Equal(409, Http.Status(await Controller(t).Terminate(created.Id, new EmployeeTerminationDto(new DateOnly(2025, 7, 1)), default)));

        var summary = Results.OkValue<EmployeeSummaryDto>(await Controller(t).Summary(default));
        Assert.Equal(0, summary.ActiveCount);
        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(0m, summary.ActiveGrossSalaryTotal);

        var reactivated = Results.OkValue<EmployeeDetailDto>(await Controller(t).Reactivate(created.Id, default));
        Assert.Equal("Active", reactivated.Status);
        Assert.Null(reactivated.TerminationDate);
    }

    [Fact]
    public async Task So_funcionario_desligado_pode_ser_excluido_e_a_exclusao_e_definitiva()
    {
        using var t = Company();
        var active = await Create(t, Dto());
        var terminated = await Create(t, Dto(name: "Pedro Alves", cpf: Cpf(6)));
        await Controller(t).Terminate(terminated.Id, new EmployeeTerminationDto(new DateOnly(2025, 6, 30)), default);

        var refused = await Controller(t).Delete(active.Id, default);
        Assert.Equal(409, Http.Status(refused));
        Assert.Contains("Desligue", Message(refused));
        Assert.Equal(204, Http.Status(await Controller(t).Delete(terminated.Id, default)));
        Assert.Equal(404, Http.Status(await Controller(t).Delete(terminated.Id, default)));

        await using var check = t.NewContext();
        Assert.Equal([active.Id], check.Employees.Select(e => e.Id).ToList());
        // Mesmo CPF pode ser cadastrado de novo depois da exclusão.
        Assert.IsType<CreatedAtActionResult>(await Controller(t).Create(Dto(name: "Pedro Alves", cpf: Cpf(6)), default));
    }

    [Fact]
    public async Task Funcionario_de_outra_empresa_nao_e_lido_nem_alterado()
    {
        using var t = Company();
        var stranger = new User { Name = "Outra Ltda", Email = "o@o.com", DocumentHash = "o", DocumentType = DocumentTypes.Cnpj, PasswordHash = "x" };
        t.Db.Users.Add(stranger);
        var foreign = new Employee
        {
            UserId = stranger.Id, FullName = "Pessoa de Fora", Cpf = Cpf(9), CpfHash = "x", Role = "Gerente",
            AdmissionDate = new DateOnly(2020, 1, 1), EmploymentType = EmploymentType.Clt, GrossSalary = 9999m,
        };
        t.Db.Employees.Add(foreign);
        t.Db.SaveChanges();
        var c = Controller(t);

        Assert.Equal(404, Http.Status(await c.Get(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.Update(foreign.Id, Dto(), default)));
        Assert.Equal(404, Http.Status(await c.SetClassification(foreign.Id, new EmployeeClassificationDto("X", null), default)));
        Assert.Equal(404, Http.Status(await c.SetPix(foreign.Id, new PayeePixDto("Email", "a@b.com"), default)));
        Assert.Equal(404, Http.Status(await c.RemovePix(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.SetBankAccount(foreign.Id, new PayeeBankDto("341", "1", "1", "Corrente"), default)));
        Assert.Equal(404, Http.Status(await c.RemoveBankAccount(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.Terminate(foreign.Id, new EmployeeTerminationDto(new DateOnly(2025, 1, 1)), default)));
        Assert.Equal(404, Http.Status(await c.Reactivate(foreign.Id, default)));
        Assert.Equal(404, Http.Status(await c.Delete(foreign.Id, default)));

        Assert.Empty(Results.OkValue<EmployeeListDto>(await c.List(null, null, null)).Items);
        Assert.Equal(0, Results.OkValue<EmployeeSummaryDto>(await c.Summary(default)).TotalCount);
        await using var check = t.NewContext();
        var untouched = check.Employees.Single(e => e.Id == foreign.Id);
        Assert.Equal("Pessoa de Fora", untouched.FullName);
        Assert.Equal(EmploymentType.Clt, untouched.EmploymentType);
        Assert.Equal(EmployeeStatus.Active, untouched.Status);
    }

    [Fact]
    public async Task Lista_pagina_no_servidor_sem_repetir_e_filtra_por_busca_situacao_e_departamento()
    {
        using var t = Company();
        for (var i = 0; i < 30; i++)
        {
            var e = await Create(t, Dto(name: $"Funcionário {i:D2}", cpf: Cpf(100 + i)));
            await Controller(t).SetClassification(e.Id, new EmployeeClassificationDto(i % 2 == 0 ? "Produção" : "Vendas", null), default);
        }
        var joao = await Create(t, Dto(name: "João Conceição", cpf: Cpf(200)));
        await Controller(t).SetClassification(joao.Id, new EmployeeClassificationDto("Vendas", null), default);
        await Controller(t).Terminate(joao.Id, new EmployeeTerminationDto(new DateOnly(2025, 1, 10)), default);

        var first = Results.OkValue<EmployeeListDto>(await Controller(t).List(null, null, null, 1, 25));
        var second = Results.OkValue<EmployeeListDto>(await Controller(t).List(null, null, null, 2, 25));
        Assert.Equal(31, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(6, second.Items.Count);
        Assert.Equal(31, first.Items.Concat(second.Items).Select(i => i.Id).Distinct().Count());

        Assert.Equal(joao.Id, Assert.Single(Results.OkValue<EmployeeListDto>(await Controller(t).List("joao conceicao", null, null)).Items).Id);
        Assert.Equal(joao.Id, Assert.Single(Results.OkValue<EmployeeListDto>(await Controller(t).List(null, "Terminated", null)).Items).Id);
        Assert.Equal(15, Results.OkValue<EmployeeListDto>(await Controller(t).List(null, "Active", "Vendas")).TotalCount);
        Assert.Equal(400, Http.Status(await Controller(t).List(null, "Ferias", null)));
        Assert.Equal(100, Results.OkValue<EmployeeListDto>(await Controller(t).List(null, null, null, 1, 10_000)).PageSize);

        var summary = Results.OkValue<EmployeeSummaryDto>(await Controller(t).Summary(default));
        Assert.Equal(30, summary.ActiveCount);
        Assert.Equal(30 * 3500.50m, summary.ActiveGrossSalaryTotal);
        Assert.Equal(["Produção", "Vendas"], summary.Departments);
    }

    [Theory]
    [InlineData("Phone", "(11) 98765-4321", "+5511987654321")]
    [InlineData("Phone", "+55 21 3456-7890", "+552134567890")]
    [InlineData("Email", " Ana@Empresa.COM ", "ana@empresa.com")]
    [InlineData("Random", "7D9F0E3A-1B2C-4D5E-8F90-A1B2C3D4E5F6", "7d9f0e3a-1b2c-4d5e-8f90-a1b2c3d4e5f6")]
    [InlineData("Cnpj", "11.222.333/0001-81", "11222333000181")]
    public void Chave_Pix_e_normalizada_antes_do_hash(string type, string raw, string expected)
    {
        Assert.Equal(expected, PayeeKeys.NormalizePixKey(Enum.Parse<PixKeyType>(type), raw));
    }
}
