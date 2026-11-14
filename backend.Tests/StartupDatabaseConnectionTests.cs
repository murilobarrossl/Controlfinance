using ControlFinance.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace ControlFinance.API.Tests;

public class StartupDatabaseConnectionTests
{
    private static IConfiguration Config(string connection, int? timeout = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Database:ConnectionTimeoutSeconds"] = timeout?.ToString()
        }).Build();

    [Fact]
    public void Timeout_padrao_preserva_SSL_e_pool()
    {
        var result = new NpgsqlConnectionStringBuilder(StartupDatabaseConnection.Configure(
            Config("Host=example.invalid;SSL Mode=VerifyFull;Maximum Pool Size=8")));
        Assert.Equal(30, result.Timeout);
        Assert.Equal(SslMode.VerifyFull, result.SslMode);
        Assert.Equal(8, result.MaxPoolSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void Timeout_explicito_nao_e_sobrescrito(int timeout)
    {
        var result = new NpgsqlConnectionStringBuilder(StartupDatabaseConnection.Configure(Config($"Host=example.invalid;Timeout={timeout}")));
        Assert.Equal(timeout, result.Timeout);
    }

    [Fact]
    public void Configuracao_permite_ajustar_timeout()
    {
        var result = new NpgsqlConnectionStringBuilder(StartupDatabaseConnection.Configure(Config("Host=example.invalid;Timeout=12", 45)));
        Assert.Equal(45, result.Timeout);
    }

    [Fact]
    public async Task Timeout_transitorio_repete_apenas_ate_a_conexao_abrir()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        await StartupDatabaseConnection.OpenAsync(_ => ++calls < 3
            ? Task.FromException(new NpgsqlException("simulado", new TimeoutException())) : Task.CompletedTask,
            NullLogger.Instance, default, (duration, _) => { delays.Add(duration); return Task.CompletedTask; });
        Assert.Equal(3, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays);
    }

    [Fact]
    public async Task Falha_persistente_tem_limite_e_nao_expoe_mensagem_do_provedor()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupDatabaseConnection.OpenAsync(_ =>
        {
            calls++;
            return Task.FromException(new NpgsqlException("informacao-privada-simulada", new TimeoutException()));
        }, NullLogger.Instance, default, (_, _) => Task.CompletedTask));
        Assert.Equal(3, calls);
        Assert.DoesNotContain("informacao-privada", error.ToString());
        Assert.Contains("Nenhuma migration", error.Message);
    }

    [Fact]
    public async Task Credencial_invalida_nao_e_repetida()
    {
        var calls = 0;
        await Assert.ThrowsAsync<PostgresException>(() => StartupDatabaseConnection.OpenAsync(_ =>
        {
            calls++;
            return Task.FromException(new PostgresException("autenticacao recusada", "FATAL", "FATAL", "28P01"));
        }, NullLogger.Instance, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancelamento_interrompe_as_tentativas()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartupDatabaseConnection.OpenAsync(_ =>
        {
            calls++;
            return Task.FromException(new TimeoutException());
        }, NullLogger.Instance, cancellation.Token, (_, _) => { cancellation.Cancel(); return Task.CompletedTask; }));
        Assert.Equal(1, calls);
    }
}
