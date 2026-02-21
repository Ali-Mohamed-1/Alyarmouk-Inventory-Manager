using System.Net;
using System.Text.Json;
using Inventory.Infrastructure.Services;
using Inventory.Application.Exceptions;


namespace Inventory.Web.Middleware;

/// <summary>
/// Catches domain exceptions and returns clean API responses (userMessage from exception, no stack traces).
/// Does not change any business logic; presentation only.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "Exception occurred but response has already started. Cannot handle gracefully.");
                throw; // Re-throw so the app/runtime handles it (likely a connection reset or log)
            }
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        string userMessage;
        HttpStatusCode statusCode;

        switch (ex)
        {
            case Application.Exceptions.ValidationException validationEx:
                userMessage = validationEx.Message;
                statusCode = HttpStatusCode.BadRequest;
                _logger.LogWarning(validationEx, "ValidationException");
                break;
            case Application.Exceptions.NotFoundException notFoundEx:
                userMessage = notFoundEx.Message;
                statusCode = HttpStatusCode.NotFound;
                _logger.LogWarning(notFoundEx, "NotFoundException");
                break;
            case Application.Exceptions.ConflictException conflictEx:
                userMessage = conflictEx.Message;
                statusCode = HttpStatusCode.Conflict;
                _logger.LogWarning(conflictEx, "ConflictException");
                break;
            default:
                userMessage = "Something went wrong. Please try again.";
                statusCode = HttpStatusCode.InternalServerError;
                _logger.LogError(ex, "Unhandled exception");
                break;
        }

        var isApi = context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

        if (isApi)
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            var body = new { userMessage };
            await context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
        else
        {
            context.Response.Redirect($"/Home/Error?message={Uri.EscapeDataString(userMessage)}");
        }
    }
}
