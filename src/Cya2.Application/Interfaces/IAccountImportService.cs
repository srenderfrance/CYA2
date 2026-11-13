using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IAccountImportService
{
    Task<AccountImportPreviewDto> PreviewAsync(Stream workbook, string fileName, CancellationToken cancellationToken = default);
    Task<AccountImportResultDto> ImportAsync(CancellationToken cancellationToken = default);
}
