using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IAccountImportService
{
    Task<AccountImportPreviewDto> PreviewAsync(CancellationToken cancellationToken = default);
    Task<AccountImportResultDto> ImportAsync(IReadOnlyList<AccountImportRowDto> rows, CancellationToken cancellationToken = default);
}
