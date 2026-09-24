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
        PostgresException? postgres = null;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException databaseError) { postgres = databaseError; break; }
        var isDomainConflict = postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.CheckViolation;
        if (!isConcurrencyConflict && !isUniqueConflict && !isDomainConflict)
        {
            return false;
        }

        var result = Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: isConcurrencyConflict || isDomainConflict ? "Cadastro alterado" : "Registro duplicado",
            detail: isConcurrencyConflict || isDomainConflict
                ? "Outro usuário alterou este cadastro. Recarregue os dados e tente novamente."
                : "O número familiar já está reservado na UBS, ou a família/imóvel já possui vínculo atual. Recarregue os dados.");
        await result.ExecuteAsync(httpContext);
        return true;
    }
}
