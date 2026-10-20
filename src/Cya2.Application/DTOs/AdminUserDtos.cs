namespace Cya2.Application.DTOs;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public int? DefaultAccountId { get; set; }
}

public class AdminUserUpdateDto
{
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? AuthLevel { get; set; }
    // Reuse requires an explicit Admin confirmation so a new or renamed user never silently inherits another intern's historical data.
    public bool ReuseExistingInternAccount { get; set; }
}

public class AdminUserOperationDto
{
    public bool IsSuccess { get; set; }
    // Signals the UI to offer reuse or allow the Admin to choose a different designation before changing anything.
    public bool RequiresInternAccountReuse { get; set; }
    public string Message { get; set; } = string.Empty;
}
