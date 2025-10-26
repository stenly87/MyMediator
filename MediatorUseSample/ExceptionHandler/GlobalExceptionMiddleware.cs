using System.ComponentModel.DataAnnotations;

namespace MediatorUseSample.ExceptionHandler
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
                //_logger.LogError(ex, "Произошла непредвиденная ошибка.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private static int GetStatusCode(Exception exception) => exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException => StatusCodes.Status400BadRequest,
            ValidationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        private static string GetErrorMessage(Exception exception) => exception switch
        {
            UnauthorizedAccessException => "Доступ запрещён: требуется аутентификация.",
            KeyNotFoundException => "Запрашиваемый ресурс не найден.",
            ArgumentException => "Некорректные входные данные.",
            ValidationException => "Ошибка валидации данных.",
            _ => "Произошла внутренняя ошибка сервера."
        };

        private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = GetStatusCode(exception);

            var response = new
            {
                error = GetErrorMessage(exception),
                detail = exception.Message
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
