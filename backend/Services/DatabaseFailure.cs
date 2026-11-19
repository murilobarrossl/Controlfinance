using Npgsql;

namespace ControlFinance.API.Services;

internal static class DatabaseFailure
{
    // Only provider failures are mapped to 503. Application/HTTP timeouts remain errors.
    internal static bool IsTransient(Exception error) => error switch
    {
        NpgsqlException provider => provider.IsTransient,
        // Sempre 503, nunca 401: não registrar a atividade é falha do serviço, não da credencial.
        ActivityNotRecordedException => true,
        InvalidOperationException { InnerException: { } inner } => IsTransient(inner),
        _ => false
    };
}
