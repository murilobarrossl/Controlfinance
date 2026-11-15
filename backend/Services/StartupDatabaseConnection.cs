using Npgsql;

namespace ControlFinance.API.Services;

internal static class StartupDatabaseConnection
{
    internal static string Configure(IConfiguration configuration)
    {
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (configuration.GetValue<int?>("Database:ConnectionTimeoutSeconds") is { } timeout)
            connection.Timeout = timeout;
        else if (!connection.ShouldSerialize("Timeout"))
            connection.Timeout = 30;

        if (configuration.GetValue<int?>("Database:MaxPoolSize") is { } maxPool)
            connection.MaxPoolSize = maxPool;
        if (configuration.GetValue<int?>("Database:ConnectionIdleLifetimeSeconds") is { } idle)
            connection.ConnectionIdleLifetime = idle;
        return connection.ConnectionString;
    }

    private static bool IsTransient(Exception error) => error switch
    {
        TimeoutException => true,
        NpgsqlException provider => provider.IsTransient,
        // A estratégia padrão do EF encapsula falhas transitórias do Npgsql.
        InvalidOperationException { InnerException: NpgsqlException provider } => provider.IsTransient,
        _ => false
    };

    internal static async Task OpenAsync(Func<CancellationToken, Task> open, ILogger logger,
        CancellationToken cancellationToken, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await open(cancellationToken);
                return;
            }
            catch (Exception error) when (IsTransient(error))
            {
                // Repete só a abertura, antes de qualquer migration/backfill. A mensagem da
                // exceção do provedor pode conter dados da conexão e não deve ir para o log.
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt == attempts)
                    throw new InvalidOperationException(
                        "Não foi possível abrir a conexão com PostgreSQL após 3 tentativas. " +
                        "Nenhuma migration ou backfill desta inicialização foi executado. " +
                        "Verifique a disponibilidade do banco, rede/VPN, host, porta e SSL da configuração local. " +
                        "O timeout pode ser ajustado em Database:ConnectionTimeoutSeconds.");
                logger.LogWarning("Falha transitória ao abrir a conexão com PostgreSQL (tentativa {Attempt}/{Attempts}). Tentando novamente antes de executar migrations.", attempt, attempts);
                await delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }
}
