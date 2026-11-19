using Microsoft.EntityFrameworkCore;
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
        // SaveChanges embrulha a falha do provedor: queda de conexão no meio de uma gravação também é 503.
        DbUpdateException { InnerException: { } inner } => IsTransient(inner),
        _ => false
    };

    // Só índice único violado é "registro repetido" (409); qualquer outra falha de gravação segue para o
    // tratador global (503 se transitória, 500 sem detalhes se não).
    internal static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
