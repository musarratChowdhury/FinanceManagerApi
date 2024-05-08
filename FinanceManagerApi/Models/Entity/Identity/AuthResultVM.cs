namespace FinanceManagerApi.Models.Entity.Identity;

public class AuthResultVM
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}