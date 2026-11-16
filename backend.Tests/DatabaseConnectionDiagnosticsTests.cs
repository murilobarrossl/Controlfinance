using System.Net.Sockets;
using System.Security.Authentication;
using ControlFinance.API.Services;
using Npgsql;

namespace ControlFinance.API.Tests;

public class DatabaseConnectionDiagnosticsTests
{
    [Fact]
    public void Cadeia_encapsulada_preserva_tipo_e_codigo_do_socket_sem_mensagens_privadas()
    {
        var error = new InvalidOperationException("Host=private;Password=private-password",
            new NpgsqlException("private-user", new SocketException((int)SocketError.NetworkUnreachable)));

        var diagnostic = DatabaseConnectionDiagnostics.Describe(error);

        Assert.Contains("InvalidOperationException -> NpgsqlException -> SocketException", diagnostic);
        Assert.Contains("SocketError=NetworkUnreachable", diagnostic);
        Assert.Contains("NativeError=", diagnostic);
        Assert.DoesNotContain("private", diagnostic);
        Assert.DoesNotContain("Password", diagnostic);
    }

    [Fact]
    public void Falha_de_autenticacao_preserva_SQLSTATE_sem_usuario_nem_mensagem()
    {
        var error = new PostgresException("password authentication failed for user private-user",
            "FATAL", "FATAL", "28P01");

        Assert.Equal("PostgresException [SQLSTATE=28P01]", DatabaseConnectionDiagnostics.Describe(error));
    }

    [Fact]
    public void Todas_as_causas_de_uma_falha_agregada_sao_diagnosticadas()
    {
        var error = new AggregateException("private-message",
            new TimeoutException("private-timeout"),
            new AuthenticationException("private-certificate"),
            new OperationCanceledException("private-cancellation"));

        var diagnostic = DatabaseConnectionDiagnostics.Describe(error);

        Assert.Contains("TimeoutException", diagnostic);
        Assert.Contains("AuthenticationException", diagnostic);
        Assert.Contains("OperationCanceledException", diagnostic);
        Assert.DoesNotContain("private", diagnostic);
    }
}
