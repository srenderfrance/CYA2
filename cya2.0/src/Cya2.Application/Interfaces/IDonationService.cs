using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Application.Interfaces;

public interface IDonationService
{
    Task<List<DonationDto>> GetDonationsByDonorAsync(string donorName, string accountFund);
    Task<List<DonationDto>> GetDonationsByAccountAsync(string accountFund, DateRange dateRange);
    Task<DonationDto> CreateDonationAsync(decimal amount, DateTime date, string donorName, string accountFund, 
                                        PaymentMethod paymentMethod, GiftType giftType, bool isAnonymous = false);
    Task<DonationDto> UpdateDonationAsync(int donationId, decimal amount);
    Task DeleteDonationAsync(int donationId);
    Task<decimal> GetTotalDonationsAsync(string accountFund, DateRange dateRange);
    Task<List<DonationDto>> GetRecentDonationsAsync(int days = 30);
}