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
}

public class AdminUserOperationDto
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
}
