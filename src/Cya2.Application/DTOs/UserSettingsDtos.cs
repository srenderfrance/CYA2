namespace Cya2.Application.DTOs;

public class UserSettingsDto
{
    public int UserId { get; set; }
    public int? DefaultAccountId { get; set; }
    public string Language { get; set; } = "en-US";
    public List<AccountOptionDto> UserAccounts { get; set; } = new();
}
