using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IUserImportService
{
    Task<UserImportPreviewDto> PreviewAsync(Stream workbook, string fileName, CancellationToken cancellationToken = default);
    Task<UserImportResultDto> ImportAsync(CancellationToken cancellationToken = default);
}
