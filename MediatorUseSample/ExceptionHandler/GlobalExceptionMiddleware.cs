using System.ComponentModel.DataAnnotations;

namespace MediatorUseSample.ExceptionHandler
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IWebHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Произошла непредвиденная ошибка. {context.Request.Method} {context.Request.Path}");
                await HandleExceptionAsync(context, ex, _environment.IsDevelopment());
            }
        }

        private static int GetStatusCode(Exception exception) => exception switch
        {
            CustomException => ((CustomException)exception).ErrorCode,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException => StatusCodes.Status400BadRequest,
            ValidationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        private static string GetErrorMessage(Exception exception) => exception switch
        {
            CustomException => ((CustomException)exception).ErrorMessage,
            UnauthorizedAccessException => "Доступ запрещён: требуется аутентификация.",
            KeyNotFoundException => "Запрашиваемый ресурс не найден.",
            ArgumentException => "Некорректные входные данные.",
            ValidationException => "Ошибка валидации данных.",
            _ => "Произошла внутренняя ошибка сервера."
        };

        private static async Task HandleExceptionAsync(HttpContext context, Exception exception, bool isDevelopment)
        {
            context.Response.ContentType = "application/json";
            var code = GetStatusCode(exception);
            context.Response.StatusCode = code;

            var response = new
            {
                Code = code,
                error = GetErrorMessage(exception),
                detail = isDevelopment ? exception.Message : null
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
