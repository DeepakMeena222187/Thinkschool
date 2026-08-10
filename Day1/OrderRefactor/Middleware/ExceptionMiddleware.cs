using Microsoft.AspNetCore.Mvc;

namespace OrderRefactor.Middleware;

public sealed class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (
            context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request cancelled. Path={Path}",
                context.Request.Path);

            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled exception. Path={Path}",
                context.Request.Path);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred."
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
