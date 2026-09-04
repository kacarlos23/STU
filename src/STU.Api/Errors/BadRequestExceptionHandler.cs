using Microsoft.AspNetCore.Diagnostics;

namespace STU.Api.Errors;

public sealed class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(
                title: "RequisiÃ§Ã£o invÃ¡lida",
                detail: "Revise os dados enviados e tente novamente.",
                statusCode: StatusCodes.Status400BadRequest)
            .ExecuteAsync(httpContext);
        return true;
    }
}
