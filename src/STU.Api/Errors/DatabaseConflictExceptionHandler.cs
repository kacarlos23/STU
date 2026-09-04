using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace STU.Api.Errors;

public sealed class DatabaseConflictExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isConcurrencyConflict = exception is DbUpdateConcurrencyException;
        var isUniqueConflict = exception is DbUpdateException
        {
            InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation },
        };
        if (!isConcurrencyConflict && !isUniqueConflict)
        {
            return false;
        }

        var result = Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: isConcurrencyConflict ? "Cadastro alterado" : "Registro duplicado",
            detail: isConcurrencyConflict
                ? "Outro usuário alterou este cadastro. Recarregue os dados e tente novamente."
                : "Já existe um cadastro ativo com estes identificadores.");
        await result.ExecuteAsync(httpContext);
        return true;
    }
}
