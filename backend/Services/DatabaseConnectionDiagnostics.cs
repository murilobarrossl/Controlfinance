using System.Data.Common;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ControlFinance.API.Services;

// ConnectionError do EF registra somente o destino. Captura a cadeia da falha também
// nas conexões abertas após o startup, sem registrar Exception.Message/ToString():
// mensagens do servidor podem incluir usuário, senha, SQL ou a connection string.
internal sealed class DatabaseConnectionDiagnostics(ILogger<DatabaseConnectionDiagnostics> logger)
    : DbConnectionInterceptor
{
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        LogFailure(eventData.Exception, false);

    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogFailure(eventData.Exception, cancellationToken.IsCancellationRequested);
        return Task.CompletedTask;
    }

    private void LogFailure(Exception error, bool cancellationRequested) =>
        logger.LogWarning("Diagnóstico da conexão PostgreSQL: {Failure}. Cancelamento solicitado: {CancellationRequested}",
            Describe(error), cancellationRequested);

    internal static string Describe(Exception error)
    {
        var detail = error switch
        {
            SocketException socket => $"SocketException [SocketError={socket.SocketErrorCode}; NativeError={socket.NativeErrorCode}]",
            PostgresException postgres => $"PostgresException [SQLSTATE={SafeSqlState(postgres.SqlState)}]",
            TimeoutException => "TimeoutException [tempo limite excedido]",
            OperationCanceledException => $"{error.GetType().Name} [operação cancelada]",
            AuthenticationException => "AuthenticationException [falha de autenticação do transporte/TLS]",
            _ => error.GetType().Name
        };
        if (error is AggregateException aggregate)
            return detail + " -> (" + string.Join(" | ", aggregate.InnerExceptions.Select(Describe)) + ")";
        return error.InnerException is { } inner ? detail + " -> " + Describe(inner) : detail;
    }

    private static string SafeSqlState(string state) =>
        state.Length == 5 && state.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z')
            ? state : "inválido";
}
