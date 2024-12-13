using System.Text.Json;
using FinanceManagerApi.Models.Errors;

namespace FinanceManagerApi.Middlewares;
// The middleware class must include:
//
//      1. A public constructor with a parameter of type RequestDelegate.
//      2. A public method named Invoke or InvokeAsync. This method must:
//      Return a Task.
//      3. Accept a first parameter of type HttpContext.
public class ExceptionMiddleware
{
    private ILogger<ExceptionMiddleware> _logger;
    private RequestDelegate _next;
    private IHostEnvironment _hostEnvironment;
    
    public ExceptionMiddleware(ILogger<ExceptionMiddleware> logger, RequestDelegate next, IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _next = next;
        _hostEnvironment = hostEnvironment;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);

            httpContext.Response.StatusCode = 500;
            httpContext.Response.ContentType = "application/json";

            var response = _hostEnvironment.IsDevelopment()
                ? new ApiExceptionResponse(httpContext.Response.StatusCode, ex.Message, ex.StackTrace?.ToString())
                : new ApiExceptionResponse(httpContext.Response.StatusCode, "An error occurred, please try again later");

            var options = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var jsonResponse = JsonSerializer.Serialize(response, options);
            await httpContext.Response.WriteAsync(jsonResponse);
        }
    }
}