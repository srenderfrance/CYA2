using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Application.Interfaces;

public interface IDonationService
{
    Task<DonationDataDto> GetDonationDataAsync(
        string accountName,
        string? subAccountSelection,
        DateRange dateRange,
        string userId,
        bool isAdminOrViewer = false,
        bool forceRefresh = false);
}