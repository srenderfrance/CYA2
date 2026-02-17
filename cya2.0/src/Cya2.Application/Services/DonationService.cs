using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Application.Services;

public class DonationService : IDonationService
{
    private readonly IDonationRepository _donationRepository;
    private readonly IDonorRepository _donorRepository;

    public DonationService(IDonationRepository donationRepository, IDonorRepository donorRepository)
    {
        _donationRepository = donationRepository;
        _donorRepository = donorRepository;
    }

    public async Task<List<DonationDto>> GetDonationsByDonorAsync(string donorName, string accountFund)
    {
        var donations = await _donationRepository.GetByDonorAndAccountAsync(donorName, accountFund);
        return donations.Select(MapToDto).ToList();
    }

    public async Task<List<DonationDto>> GetDonationsByAccountAsync(string accountFund, DateRange dateRange)
    {
        var donations = await _donationRepository.GetByAccountFundAsync(accountFund);
        var filtered = donations.Where(d => dateRange.Contains(d.Date)).ToList();
        return filtered.Select(MapToDto).ToList();
    }

    public async Task<DonationDto> CreateDonationAsync(decimal amount, DateTime date, string donorName, 
        string accountFund, PaymentMethod paymentMethod, GiftType giftType, bool isAnonymous = false)
    {
        var donation = new Donation(amount, date, donorName, accountFund, paymentMethod, giftType, isAnonymous);
        var savedDonation = await _donationRepository.AddAsync(donation);

        // Ensure donor exists or create new one
        var donor = await _donorRepository.GetByNameAsync(donorName);
        if (donor == null)
        {
            donor = new Donor(donorName);
            await _donorRepository.AddAsync(donor);
        }

        return MapToDto(savedDonation);
    }

    public async Task<DonationDto> UpdateDonationAsync(int donationId, decimal amount)
    {
        var donations = await _donationRepository.GetAllAsync();
        var donation = donations.FirstOrDefault(d => d.Id == donationId);
        
        if (donation == null)
            throw new ArgumentException($"Donation with ID {donationId} not found");

        donation.ChangeAmount(amount);
        var updatedDonation = await _donationRepository.UpdateAsync(donation);
        return MapToDto(updatedDonation);
    }

    public async Task DeleteDonationAsync(int donationId)
    {
        await _donationRepository.DeleteAsync(donationId);
    }

    public async Task<decimal> GetTotalDonationsAsync(string accountFund, DateRange dateRange)
    {
        return await _donationRepository.GetTotalByAccountAsync(accountFund, dateRange);
    }

    public async Task<List<DonationDto>> GetRecentDonationsAsync(int days = 30)
    {
        var donations = await _donationRepository.GetRecentDonationsAsync(days);
        return donations.Select(MapToDto).ToList();
    }

    public async Task<decimal> GetTotalGivingAsync(string accountFund, DateRange dateRange)
    {
        return await _donationRepository.GetTotalByAccountFundAsync(accountFund, dateRange.StartDate, dateRange.EndDate);
    }

    private static DonationDto MapToDto(Donation donation)
    {
        return new DonationDto
        {
            Id = donation.Id,
            Amount = donation.Amount,
            Date = donation.Date,
            DonorName = donation.DonorName,
            AccountFund = donation.AccountFund,
            PaymentMethod = donation.PaymentMethod,
            GiftType = donation.GiftType,
            SoftCreditName = donation.SoftCreditName,
            IsAnonymous = donation.IsAnonymous,
            DateCreated = donation.DateCreated
        };
    }
}