using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class DonorExportService : IDonorExportService
{
    private readonly IDonorService _donorService;
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IUserAuthorizationService _authorizationService;

    public DonorExportService(
        IDonorService donorService,
        IUserAccountContextService userAccountContextService,
        IUserAuthorizationService authorizationService)
    {
        _donorService = donorService;
        _userAccountContextService = userAccountContextService;
        _authorizationService = authorizationService;
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

        foreach (var fund in requestedFunds)
        {
            if (!(await _authorizationService.ValidateAccountAccessAsync(userId, fund)).IsAuthorized)
                return DonorExportResult.Forbidden(requestedFunds);
        }

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

    public async Task<DonorExportResult> GetSelectionExportDataAsync(
        string userId,
        bool isAdminOrViewerHint,
        int accountId,
        string selectedSubAccount,
        bool allDates,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (!(await _authorizationService.ValidateAccountAccessAsync(userId, accountId)).IsAuthorized)
            return DonorExportResult.Forbidden(Array.Empty<string>());

        var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewerHint);
        if (context == null)
            return DonorExportResult.MissingUserContext(Array.Empty<string>());

        var account = context.Accounts.FirstOrDefault(a => a.AccountId == accountId);
        if (account == null)
            return DonorExportResult.Forbidden(Array.Empty<string>());

        var option = new AccountOptionDto
        {
            AccountId = account.AccountId,
            Fund = account.Fund,
            InternName = account.InternName,
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            Overhead = account.Overhead
        };
        var range = allDates ? null : new DateRange(startDate ?? DateTime.MinValue, endDate ?? DateTime.MaxValue);
        var donors = await _donorService.GetDonorSummariesForSelectionAsync(option, selectedSubAccount, range, userId);
        return new DonorExportResult(true, true, Array.Empty<string>(), donors);
    }
}
