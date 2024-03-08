using Microsoft.AspNetCore.Identity;

namespace FinanceManagerApi.Models.Entity.Identity;

public class UserProfile : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
}