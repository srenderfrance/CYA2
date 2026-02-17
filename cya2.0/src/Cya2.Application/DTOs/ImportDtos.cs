using Cya2.Application.Interfaces.External;

namespace Cya2.Application.DTOs;

// Core DTOs for clean architecture that don't conflict with main application
public class UserDto : IUser
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Clean architecture specific analytics (non-conflicting)
public class DonorAnalytics
{
    public decimal TotalAmount { get; set; }
    public int UniqueAccounts { get; set; }
    public int UniqueDonors { get; set; }
    public DateTime OldestTransaction { get; set; }
    public DateTime NewestTransaction { get; set; }
}

public class AccountImpact
{
    public string AccountFund { get; set; } = string.Empty;
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public decimal Change => NewBalance - PreviousBalance;
}