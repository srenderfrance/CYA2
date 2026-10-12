namespace Cya2.Application.DTOs;

public sealed class AccountAccessUserDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string AuthLevel { get; init; } = string.Empty;
    public bool IsGlobalViewer { get; init; }
}
