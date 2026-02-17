using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Dapper;
using System.Data;

namespace Cya2.Infrastructure.Data.Repositories;

public class DonorRepository : BaseRepository, IDonorRepository
{
    public DonorRepository(IDataAccess dataAccess, IConfiguration configuration, ILogger<DonorRepository> logger) 
        : base(dataAccess, configuration, logger)
    {
    }

    public async Task<Donor?> GetByIdAsync(int id)
    {
        // Since we don't have donor IDs in current schema, this is not directly supported
        // Could potentially map to donor names if we had a mapping
        _logger.LogWarning("GetByIdAsync not supported with current schema - use GetByNameAsync instead");
        throw new NotSupportedException("GetByIdAsync not supported with current schema - use GetByNameAsync instead");
    }

    public async Task<Donor?> GetByNameAsync(string name)
    {
        try
        {
            // First get basic donor info - we'll build this from donation data for now
            // since your current schema doesn't have a separate donors table
            const string sql = @"
                SELECT DISTINCT AccountName as Name
                FROM Donations 
                WHERE AccountName = @Name
                LIMIT 1";

            var donorNames = await LoadDataAsync<string, object>(sql, new { Name = name });
            var donorName = donorNames.FirstOrDefault();
            
            if (string.IsNullOrWhiteSpace(donorName))
                return null;

            var donor = new Donor(donorName);
            
            // Load donations for this donor and set contact info
            await LoadDonorDataAsync(donor, donorName);

            return donor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting donor by name: {Name}", name);
            throw;
        }
    }

    public async Task<List<Donor>> GetAllAsync()
    {
        try
        {
            const string sql = @"
                SELECT DISTINCT AccountName as Name
                FROM Donations 
                WHERE AccountName IS NOT NULL AND AccountName != ''
                ORDER BY AccountName";

            var donorNames = await LoadDataAsync<string, object>(sql, new { });
            var donors = new List<Donor>();

            foreach (var name in donorNames)
            {
                var donor = await GetByNameAsync(name);
                if (donor != null)
                    donors.Add(donor);
            }

            return donors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all donors");
            throw;
        }
    }

    public async Task<List<Donor>> GetByAccountAsync(string accountFund)
    {
        try
        {
            const string sql = @"
                SELECT DISTINCT AccountName as Name
                FROM Donations 
                WHERE Fund = @AccountFund 
                  AND AccountName IS NOT NULL AND AccountName != ''
                ORDER BY AccountName";

            var donorNames = await LoadDataAsync<string, object>(sql, new { AccountFund = accountFund });
            var donors = new List<Donor>();

            foreach (var name in donorNames)
            {
                var donor = await GetByNameAsync(name);
                if (donor != null)
                    donors.Add(donor);
            }

            return donors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting donors by account: {AccountFund}", accountFund);
            throw;
        }
    }

    public async Task<List<Donor>> GetActiveAsync(DateTime asOfDate)
    {
        try
        {
            var cutoffDate = asOfDate.AddMonths(-24); // Active = donated in last 24 months
            
            const string sql = @"
                SELECT DISTINCT AccountName as Name
                FROM Donations 
                WHERE Date > @CutoffDate 
                  AND AccountName IS NOT NULL AND AccountName != ''
                ORDER BY AccountName";

            var donorNames = await LoadDataAsync<string, object>(sql, new { CutoffDate = cutoffDate });
            var donors = new List<Donor>();

            foreach (var name in donorNames)
            {
                var donor = await GetByNameAsync(name);
                if (donor != null)
                    donors.Add(donor);
            }

            return donors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active donors as of: {AsOfDate}", asOfDate);
            throw;
        }
    }

    public async Task<List<Donor>> SearchAsync(string searchTerm)
    {
        try
        {
            const string sql = @"
                SELECT DISTINCT AccountName as Name
                FROM Donations 
                WHERE AccountName IS NOT NULL 
                  AND AccountName != ''
                  AND AccountName LIKE @SearchTerm
                ORDER BY AccountName";

            var donorNames = await LoadDataAsync<string, object>(sql, new { SearchTerm = $"%{searchTerm}%" });
            var donors = new List<Donor>();

            foreach (var name in donorNames)
            {
                var donor = await GetByNameAsync(name);
                if (donor != null)
                    donors.Add(donor);
            }

            return donors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching donors with term: {SearchTerm}", searchTerm);
            throw;
        }
    }

    public async Task<Donor> AddAsync(Donor donor)
    {
        // Since we don't have a separate donors table, we don't need to insert anything
        // The donor exists implicitly through their donations
        _logger.LogInformation("Donor {DonorName} added to domain model", donor.Name);
        return donor;
    }

    public async Task<Donor> UpdateAsync(Donor donor)
    {
        // For now, donor updates would need to update donation records
        // This is a limitation of the current schema
        _logger.LogInformation("Donor {DonorName} updated in domain model", donor.Name);
        return donor;
    }

    public async Task DeleteAsync(int id)
    {
        // Since donors are implicit, we can't delete them directly
        // This would require deleting all donations for the donor
        throw new NotSupportedException("Donor deletion not supported with current schema");
    }

    public async Task<bool> ExistsAsync(int id)
    {
        // Since we don't have donor IDs in current schema, this is not directly supported
        throw new NotSupportedException("ExistsAsync(int) not supported with current schema - use ExistsAsync(string) instead");
    }

    public async Task<bool> ExistsAsync(string name)
    {
        try
        {
            const string sql = @"
                SELECT COUNT(1) 
                FROM Donations 
                WHERE AccountName = @Name";

            var counts = await LoadDataAsync<int, object>(sql, new { Name = name });
            return counts.FirstOrDefault() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if donor exists: {Name}", name);
            throw;
        }
    }

    private async Task LoadDonorDataAsync(Donor donor, string donorName)
    {
        try
        {
            // Load donations for this donor
            var donations = await GetDonationsForDonorAsync(donorName);
            foreach (var donation in donations)
            {
                donor.AddDonation(donation);
            }

            // Set contact info from most recent donation with contact data
            await SetContactInfoFromDonationsAsync(donor, donorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading data for donor: {DonorName}", donorName);
            throw;
        }
    }

    private async Task<List<Donation>> GetDonationsForDonorAsync(string donorName)
    {
        const string sql = @"
            SELECT Id, Date, AccountName, PaymentMethod, GiftType, 
                   Amount, Fund, SoftCreditName, Address, City, State, 
                   PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous
            FROM Donations 
            WHERE AccountName = @DonorName
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { DonorName = donorName });
        var donations = new List<Donation>();

        foreach (var data in donationData)
        {
            var donation = new Donation(
                Convert.ToDouble(data.Amount), 
                data.Date, 
                data.AccountName?.ToString() ?? donorName,
                data.Fund?.ToString() ?? "",
                data.PaymentMethod?.ToString() ?? "",
                data.GiftType?.ToString() ?? "",
                data.IsAnonymous
            );

            if (!string.IsNullOrWhiteSpace(data.SoftCreditName?.ToString()))
            {
                donation.SetSoftCredit(data.SoftCreditName.ToString());
            }

            donations.Add(donation);
        }

        return donations;
    }

    private async Task SetContactInfoFromDonationsAsync(Donor donor, string donorName)
    {
        const string sql = @"
            SELECT Email, PhoneMobile, PhoneFixed, Address, City, State, PostalCode, Country
            FROM Donations 
            WHERE AccountName = @DonorName 
              AND (Email IS NOT NULL OR PhoneMobile IS NOT NULL OR PhoneFixed IS NOT NULL OR Address IS NOT NULL)
            ORDER BY Date DESC
            LIMIT 1";

        var contactData = await QueryFirstOrDefaultAsync<dynamic>(sql, new { DonorName = donorName });
        
        if (contactData != null)
        {
            donor.UpdateContactInfo(
                contactData.Email?.ToString(),
                contactData.PhoneMobile?.ToString(),
                contactData.PhoneFixed?.ToString(),
                contactData.Address?.ToString(),
                contactData.City?.ToString(),
                contactData.State?.ToString(),
                contactData.PostalCode?.ToString(),
                contactData.Country?.ToString()
            );
        }
    }
}