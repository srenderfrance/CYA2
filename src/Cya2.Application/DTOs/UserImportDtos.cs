namespace Cya2.Application.DTOs;

public sealed record UserImportRowDto(int RowNumber, string Email, string Name, string AuthLevel);

public sealed class UserImportPreviewDto
{
    public List<UserImportRowDto> Rows { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool CanImport => Rows.Count > 0 && Errors.Count == 0;
}

public sealed record UserImportResultDto(bool IsSuccess, string Message);
