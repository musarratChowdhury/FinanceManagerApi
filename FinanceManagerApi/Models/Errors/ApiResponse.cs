namespace FinanceManagerApi.Models.Errors;

public class ApiResponse
{
    public int? StatusCode { get; set; }
    public string? Message { get; set; }

    public ApiResponse(int statusCode, string? message)
    {
        StatusCode = statusCode;
        Message = message ?? GetDefaultMessageForStatusCode(statusCode);
    }
    
    public string? GetDefaultMessageForStatusCode(int statusCode)
    {
        return StatusCode switch
        {
            400 => "Bad Request",
            401 => "You Are Not Authorized",
            402 => "Resource Not Found",
            403 => "Forbidden",
            500 => "Internal Server Error",
            _ => "An unexpected error occurred"
        };
    }
}