using System.Net;
using System.Text;
using ControlFinance.API.Controllers;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ControlFinance.API.Tests;

public class PolpConsentValidationErrorTests
{
    private const string Cnpj = "11222333000181";
    private const string OperatorCpf = "52998224725";

    // Formato de erro de validação que a Polp usa (message + errors por campo), com o documento
    // repetido no texto, que é exatamente o que não pode chegar ao log.
    private const string ValidationBody =
        "{\"message\":\"O cnpj 11.222.333/0001-81 é inválido.\",\"code\":\"VALIDATION_ERROR\"," +
        "\"errors\":{\"cnpj\":[\"O cnpj 11222333000181 não pertence ao cpf 52998224725.\"],\"<script>\":[\"x\"]}}";

    private sealed class Respond(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static PolpService Service(HttpMessageHandler handler, ListLogger<PolpService> logger)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Polp:BaseUrl"] = "https://polp.invalid/api/v2/sandbox",
            ["Polp:ClientId"] = "id",
            ["Polp:ClientSecret"] = "secret",
        }).Build();
        return new PolpService(new HttpClient(handler), config, logger);
    }

    [Fact]
    public void Resumo_traz_codigo_campos_e_mensagens_sem_nenhum_digito()
    {
        var summary = PolpValidationSummary.From(ValidationBody);

        Assert.Equal("VALIDATION_ERROR", summary.Code);
        Assert.Equal(["cnpj"], summary.Fields);
        Assert.All(summary.Messages, m => Assert.DoesNotMatch(@"\d", m));
        Assert.Contains(summary.Messages, m => m.Contains("não pertence ao cpf"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>erro 52998224725</html>")]
    [InlineData("[\"52998224725\"]")]
    public void Corpo_que_nao_e_objeto_JSON_nao_gera_resumo(string body)
    {
        var summary = PolpValidationSummary.From(body);

        Assert.Null(summary.Code);
        Assert.Empty(summary.Fields);
        Assert.Empty(summary.Messages);
    }

    [Fact]
    public async Task Criacao_recusada_com_422_vira_excecao_com_os_campos_e_log_sem_documentos()
    {
        var logger = new ListLogger<PolpService>();
        var service = Service(new Respond(HttpStatusCode.UnprocessableEntity, ValidationBody), logger);

        var ex = await Assert.ThrowsAsync<PolpValidationException>(() =>
            service.CreateConsentAsync(new CreatePolpConsentRequest(Guid.NewGuid(), Cnpj, Guid.NewGuid(), OperatorCpf)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Equal(["cnpj"], ex.Fields);
        Assert.DoesNotMatch(@"\d", ex.Message.Replace("422", ""));
        var log = Assert.Single(logger.Entries).Message;
        Assert.Contains("VALIDATION_ERROR", log);
        Assert.Contains("cnpj", log);
        Assert.DoesNotContain("11222333", log);
        Assert.DoesNotContain("52998224725", log);
        Assert.DoesNotContain("11.222.333", log);
    }

    [Fact]
    public async Task Outros_erros_continuam_sem_ler_o_corpo()
    {
        var logger = new ListLogger<PolpService>();
        var service = Service(new Respond(HttpStatusCode.BadRequest, ValidationBody), logger);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CreateConsentAsync(new CreatePolpConsentRequest(Guid.NewGuid(), Cnpj, Guid.NewGuid(), OperatorCpf)));

        Assert.IsNotType<PolpValidationException>(ex);
        Assert.DoesNotContain("cnpj", Assert.Single(logger.Entries).Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Credencial_recusada_vira_erro_no_log_com_o_nome_das_chaves_e_sem_o_valor(HttpStatusCode status)
    {
        var logger = new ListLogger<PolpService>();
        var service = Service(new Respond(status, "{\"message\":\"Invalid API credentials.\"}"), logger);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CreateConsentAsync(new CreatePolpConsentRequest(Guid.NewGuid(), Cnpj, Guid.NewGuid(), OperatorCpf)));

        Assert.Equal(status, ex.StatusCode);
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, level);
        Assert.Contains("credenciais recusadas", message);
        Assert.Contains("Polp:ClientSecret", message);
        // "secret" é o valor de Polp:ClientSecret em Service(); o log só pode citar o nome da chave.
        Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Cnpj, message);
    }

    [Theory]
    [InlineData("cnpj", "CNPJ")]
    [InlineData("cpf", "CPF de quem opera")]
    [InlineData("institution_id", "Esse banco")]
    public async Task Connect_de_empresa_explica_qual_dado_a_Polp_recusou(string field, string expected)
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cnpj);
        h.T.User.DocumentType = DocumentTypes.Cnpj;
        h.T.Db.SaveChanges();
        var (service, polp) = FakePolpProxy.Create();
        polp.CreateConsentError = new PolpValidationException("Falha ao criar consentimento na Polp (HTTP 422).", [field]);
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var controller = h.T.Controller(db => new PolpController(
            db, service, new InstitutionNameResolver(service, cache, new ListLogger<InstitutionNameResolver>()),
            h.T.Encryption, null!, h.Inbox, cache));

        var result = await controller.Connect(
            new PolpConnectDto(Guid.NewGuid().ToString(), Cnpj, LegalDocuments.BankConnectionNoticeVersion, OperatorCpf), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(expected, Http.Json(result));
        Assert.DoesNotContain(Cnpj, Http.Json(result));
    }
}
