using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IDonorExportService
{
    Task<DonorExportResult> GetExportDataAsync(
        string userId,
        bool isAdminOrViewerHint,
        IEnumerable<string> funds,
        bool allDates,
        DateTime? startDate,
        DateTime? endDate);
}

public sealed record DonorExportResult(
    bool UserContextFound,
    bool FundsAuthorized,
    IReadOnlyList<string> RequestedFunds,
    IReadOnlyList<DonorSummaryDto> Donors)
{
    public static DonorExportResult MissingUserContext(IReadOnlyList<string> funds) =>
        new(false, false, funds, Array.Empty<DonorSummaryDto>());

    public static DonorExportResult Forbidden(IReadOnlyList<string> funds) =>
        new(true, false, funds, Array.Empty<DonorSummaryDto>());
}
