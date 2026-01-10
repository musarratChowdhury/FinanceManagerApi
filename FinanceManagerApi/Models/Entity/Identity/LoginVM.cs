using System.ComponentModel.DataAnnotations;

namespace FinanceManagerApi.Models.Entity.Identity;

public class LoginVM
{
    [Required]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}