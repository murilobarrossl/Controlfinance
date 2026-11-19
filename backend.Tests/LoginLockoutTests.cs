using System.Net;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class LoginLockoutTests
{
    private const string Password = "senha1234";
    private const string Email = "joana@teste.com";
    private const string Ip = "127.0.0.1";

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class FakeTokens : ITokenService
    {
        public IssuedToken GenerateToken(User user) => new("jwt", "csrf", DateTime.UtcNow.AddHours(1));
    }

    private static async Task<AuthService> RegisteredService(TestDb t, RateLimitService rateLimit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:ApiKey"] = "teste" })
            .Build();
        var service = new AuthService(t.Db, new FakeTokens(), new EmailService(config, new HttpClient(new OkHandler())),
            rateLimit, t.Encryption, NullLogger<AuthService>.Instance);
        var registered = await service.RegisterAsync(new RegisterRequestDto
        {
            Name = "Joana",
            Email = Email,
            Document = "52998224725",
            Password = Password,
            AcceptedTermsVersion = LegalDocuments.TermsVersion,
            AcceptedPrivacyVersion = LegalDocuments.PrivacyVersion,
            ConfirmsAdult = true,
        }, Ip);
        Assert.True(registered.Success);
        return service;
    }

    private static Task<(bool Success, string Error, AuthResult? Data, TimeSpan? LockedFor)> Login(
        AuthService service, string identifier, string password) =>
        service.LoginAsync(new LoginRequestDto { Identifier = identifier, Password = password }, Ip);

    [Fact]
    public async Task Senha_errada_antes_do_limite_nao_informa_bloqueio()
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();
        var service = await RegisteredService(t, rateLimit);

        for (var i = 0; i < 4; i++)
        {
            var result = await Login(service, Email, "errada");
            Assert.False(result.Success);
            Assert.Null(result.LockedFor);
            Assert.Equal("Credenciais inválidas.", result.Error);
        }
    }

    [Fact]
    public async Task Quinta_falha_bloqueia_e_informa_o_tempo_restante()
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();
        var service = await RegisteredService(t, rateLimit);

        for (var i = 0; i < 4; i++) await Login(service, Email, "errada");
        var result = await Login(service, Email, "errada");

        Assert.False(result.Success);
        Assert.NotNull(result.LockedFor);
        Assert.InRange(result.LockedFor!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.Equal("Muitas tentativas de login. Aguarde 15 minutos antes de tentar novamente.", result.Error);
    }

    [Fact]
    public async Task Senha_certa_durante_o_bloqueio_continua_recusada_como_bloqueio()
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();
        var service = await RegisteredService(t, rateLimit);

        for (var i = 0; i < 5; i++) await Login(service, Email, "errada");
        var result = await Login(service, Email, Password);

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.NotNull(result.LockedFor);
    }

    [Fact]
    public async Task Identificador_sem_conta_bloqueia_igual_e_nao_revela_cadastro()
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();
        var service = await RegisteredService(t, rateLimit);

        (bool Success, string Error, AuthResult? Data, TimeSpan? LockedFor) existing = default, missing = default;
        for (var i = 0; i < 5; i++)
        {
            existing = await Login(service, Email, "errada");
            missing = await Login(service, "ninguem@teste.com", "errada");
        }

        Assert.Equal(existing.Error, missing.Error);
        Assert.NotNull(missing.LockedFor);
    }
}
