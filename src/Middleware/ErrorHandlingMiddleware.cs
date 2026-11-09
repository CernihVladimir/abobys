using System.Net;
using System.Text.Json;

namespace LmsPlatform.Middleware
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var code = HttpStatusCode.InternalServerError;
            var message = "An internal server error occurred.";

            if (exception is InvalidOperationException invalidOpEx)
            {
                code = HttpStatusCode.BadRequest;
                message = invalidOpEx.Message;
            }
            else if (exception is UnauthorizedAccessException unauthEx)
            {
                code = HttpStatusCode.Unauthorized;
                message = unauthEx.Message;
            }
            else if (exception is KeyNotFoundException notFoundEx)
            {
                code = HttpStatusCode.NotFound;
                message = notFoundEx.Message;
            }

            var result = JsonSerializer.Serialize(new
            {
                statusCode = (int)code,
                message = message,
                details = exception.Message
            });

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;

            return context.Response.WriteAsync(result);
        }
    }
}
