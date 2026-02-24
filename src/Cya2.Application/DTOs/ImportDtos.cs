using System.ComponentModel.DataAnnotations;

namespace Cya2.Application.DTOs;

// Import-related DTOs
public class ImportRequestDto
{
    public string ImportType { get; set; } = string.Empty; // "Donation", "Accounting"
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    // Removed IUser dependency - will use string UserId instead
    public string RequestedBy { get; set; } = string.Empty;
}

public class ImportSummaryDto
{
    public string ImportId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Pending", "Processing", "Completed", "Failed"
    public int TotalRecords { get; set; }
    public int SuccessfulRecords { get; set; }
    public int FailedRecords { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ImportResultDto
{
    public string ImportId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ProcessedRecords { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

// User-related DTOs  
public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}