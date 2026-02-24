using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class SubAccountDto
{
    public int Id { get; set; }
    public string SubFund { get; set; } = string.Empty;
    public SubAccountType Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}