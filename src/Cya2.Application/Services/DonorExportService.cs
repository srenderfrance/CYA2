using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class DonorExportService : IDonorExportService
{
    private readonly IDonorService _donorService;
    private readonly IUserAccountContextService _userAccountContextService;

    public DonorExportService(
        IDonorService donorService,
        IUserAccountContextService userAccountContextService)
    {
        _donorService = donorService;
        _userAccountContextService = userAccountContextService;
    }

    public async Task<DonorExportResult> GetExportDataAsync(
        string userId,
        bool isAdminOrViewerHint,
        IEnumerable<string> funds,
        bool allDates,
        DateTime? startDate,
        DateTime? endDate)
    {
        var requestedFunds = funds
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewerHint);
        if (context == null)
        {
            return DonorExportResult.MissingUserContext(requestedFunds);
        }

        var allowedFunds = context.Accounts
            .Select(a => a.Fund)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedFunds.Any(f => !allowedFunds.Contains(f)))
        {
            return DonorExportResult.Forbidden(requestedFunds);
        }

        var donorRows = allDates
            ? await _donorService.GetAllDonorSummariesAsync(requestedFunds)
            : await _donorService.GetDonorSummariesAsync(
                requestedFunds,
                new DateRange(
                    startDate ?? DateTime.MinValue,
                    endDate ?? DateTime.MaxValue));

        return new DonorExportResult(true, true, requestedFunds, donorRows ?? new List<DonorSummaryDto>());
    }
}
