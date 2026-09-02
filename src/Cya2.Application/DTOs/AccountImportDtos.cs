namespace Cya2.Application.DTOs;

public sealed class AccountImportRowDto
{
    public int RowNumber { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string Fund { get; init; } = string.Empty;
    public string AccountingClass { get; init; } = string.Empty;
    public decimal Overhead { get; init; }
}

public sealed class AccountImportPreviewDto
{
    public string FileName { get; init; } = string.Empty;
    public List<AccountImportRowDto> Rows { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool CanImport => Rows.Count > 0 && Errors.Count == 0;
}

public sealed class AccountImportResultDto
{
    public bool IsSuccess { get; init; }
    public int ImportedCount { get; init; }
    public string Message { get; init; } = string.Empty;
}
