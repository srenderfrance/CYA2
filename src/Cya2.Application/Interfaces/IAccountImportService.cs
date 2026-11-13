using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IAccountImportService
{
    Task<AccountImportPreviewDto> PreviewAsync(CancellationToken cancellationToken = default);
    Task<AccountImportResultDto> ImportAsync(CancellationToken cancellationToken = default);
}
