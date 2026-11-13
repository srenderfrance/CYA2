namespace Cya2.Application.DTOs;

public sealed record UserImportRowDto(int RowNumber, string Email, string Name, string AuthLevel, bool IsExisting = false);

public sealed class UserImportPreviewDto
{
    public List<UserImportRowDto> Rows { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public int SkippedCount => Rows.Count(row => row.IsExisting);
    public bool CanImport => Rows.Any(row => !row.IsExisting) && Errors.Count == 0;
}

public sealed record UserImportResultDto(bool IsSuccess, string Message);
