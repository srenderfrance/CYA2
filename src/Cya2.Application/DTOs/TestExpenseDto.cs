namespace Cya2.Application.DTOs;

/// <summary>
/// Test DTO for debugging compilation issues
/// </summary>
public class TestExpenseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}