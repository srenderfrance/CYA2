using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Infrastructure.Data.Repositories;

public class DonationRepository : BaseRepository, IDonationRepository
{
    public DonationRepository(IDataAccess dataAccess, IConfiguration configuration, ILogger<DonationRepository> logger) 
        : base(dataAccess, configuration, logger)
    {
    }

    public async Task<List<Donation>> GetByDonorNameAsync(string donorName)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE AccountName = @DonorName
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { DonorName = donorName });
        return MapToDomainEntities(donationData);
    }

    public async Task<List<Donation>> GetByAccountFundAsync(string accountFund)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Fund = @AccountFund
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { AccountFund = accountFund });
        return MapToDomainEntities(donationData);
    }

    public async Task<List<Donation>> GetByDateRangeAsync(DateRange dateRange)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Date >= @StartDate AND Date <= @EndDate
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { 
            StartDate = dateRange.StartDate, 
            EndDate = dateRange.EndDate 
        });
        return MapToDomainEntities(donationData);
    }

    public async Task<List<Donation>> GetByDonorAndAccountAsync(string donorName, string accountFund)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE AccountName = @DonorName AND Fund = @AccountFund
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { 
            DonorName = donorName, 
            AccountFund = accountFund 
        });
        return MapToDomainEntities(donationData);
    }

    public async Task<List<Donation>> GetAllAsync()
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { });
        return MapToDomainEntities(donationData);
    }

    public async Task<Donation> AddAsync(Donation donation)
    {
        const string sql = @"
            INSERT INTO Donations (Date, AccountName, PaymentMethod, GiftType, Amount, Fund, 
                                 SoftCreditName, DateCreated, IsAnonymous)
            VALUES (@Date, @DonorName, @PaymentMethod, @GiftType, @Amount, @AccountFund, 
                    @SoftCreditName, @DateCreated, @IsAnonymous)";

        var parameters = new
        {
            Date = donation.Date,
            DonorName = donation.DonorName,
            PaymentMethod = donation.PaymentMethod.ToString(),
            GiftType = donation.GiftType.ToString(),
            Amount = donation.Amount,
            AccountFund = donation.AccountFund,
            SoftCreditName = donation.SoftCreditName,
            DateCreated = donation.DateCreated,
            IsAnonymous = donation.IsAnonymous
        };

        var affectedRows = await SaveDataAsync(sql, parameters);
        
        if (affectedRows > 0)
        {
            _logger.LogInformation("Created donation for {DonorName}", donation.DonorName);
        }
        
        return donation;
    }

    public async Task<Donation> UpdateAsync(Donation donation)
    {
        const string sql = @"
            UPDATE Donations 
            SET Date = @Date, AccountName = @DonorName, PaymentMethod = @PaymentMethod, 
                GiftType = @GiftType, Amount = @Amount, Fund = @AccountFund, 
                SoftCreditName = @SoftCreditName, IsAnonymous = @IsAnonymous
            WHERE Id = @Id";

        var parameters = new
        {
            Id = donation.Id,
            Date = donation.Date,
            DonorName = donation.DonorName,
            PaymentMethod = donation.PaymentMethod.ToString(),
            GiftType = donation.GiftType.ToString(),
            Amount = donation.Amount,
            AccountFund = donation.AccountFund,
            SoftCreditName = donation.SoftCreditName,
            IsAnonymous = donation.IsAnonymous
        };

        var affectedRows = await SaveDataAsync(sql, parameters);
        
        if (affectedRows == 0)
            throw new ArgumentException($"Donation with ID {donation.Id} not found");

        _logger.LogInformation("Updated donation {DonationId}", donation.Id);
        return donation;
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM Donations WHERE Id = @Id";
        
        var affectedRows = await SaveDataAsync(sql, new { Id = id });
        
        if (affectedRows == 0)
            throw new ArgumentException($"Donation with ID {id} not found");

        _logger.LogInformation("Deleted donation {DonationId}", id);
    }

    public async Task<decimal> GetTotalByAccountAsync(string accountFund, DateRange dateRange)
    {
        const string sql = @"
            SELECT COALESCE(SUM(Amount), 0)
            FROM Donations 
            WHERE Fund = @AccountFund 
              AND Date >= @StartDate AND Date <= @EndDate";

        var result = await LoadDataAsync<decimal>(sql, new { 
            AccountFund = accountFund,
            StartDate = dateRange.StartDate, 
            EndDate = dateRange.EndDate 
        });
        
        return result.FirstOrDefault();
    }

    public async Task<List<Donation>> GetRecentDonationsAsync(int days = 30)
    {
        var cutoffDate = DateTime.Today.AddDays(-days);
        
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Date >= @CutoffDate
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { CutoffDate = cutoffDate });
        return MapToDomainEntities(donationData);
    }

    // Add missing methods from interface
    public async Task<Donation?> GetByIdAsync(int id)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Id = @Id";

        var data = await QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });
        
        if (data == null) return null;

        var donations = MapToDomainEntities(new[] { data });
        return donations.FirstOrDefault();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        const string sql = "SELECT COUNT(1) FROM Donations WHERE Id = @Id";
        var result = await LoadDataAsync<int>(sql, new { Id = id });
        return result.FirstOrDefault() > 0;
    }

    public async Task<List<Donation>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Date >= @StartDate AND Date <= @EndDate
            ORDER BY Date DESC";

        var donationData = await QueryAsync<dynamic>(sql, new { 
            StartDate = startDate, 
            EndDate = endDate 
        });
        return MapToDomainEntities(donationData);
    }

    public async Task<List<Donation>> GetByDonorAsync(string donorName)
    {
        return await GetByDonorNameAsync(donorName);
    }

    public async Task<decimal> GetTotalByAccountFundAsync(string accountFund, DateTime startDate, DateTime endDate)
    {
        const string sql = @"
            SELECT COALESCE(SUM(Amount), 0)
            FROM Donations 
            WHERE Fund = @AccountFund 
              AND Date >= @StartDate 
              AND Date <= @EndDate";

        var result = await LoadDataAsync<decimal>(sql, 
            new { AccountFund = accountFund, StartDate = startDate, EndDate = endDate });

        return result.FirstOrDefault();
    }

    private static List<Donation> MapToDomainEntities(IEnumerable<dynamic> donationData)
    {
        var donations = new List<Donation>();

        foreach (var data in donationData)
        {
            try
            {
                var paymentMethod = ParsePaymentMethod(data.PaymentMethod?.ToString());
                var giftType = ParseGiftType(data.GiftType?.ToString());
                
                var donation = new Donation(
                    Convert.ToDecimal(data.Amount), 
                    data.Date, 
                    data.DonorName?.ToString() ?? "",
                    data.AccountFund?.ToString() ?? "",
                    paymentMethod,
                    giftType,
                    data.IsAnonymous
                );

                // Set ID using reflection (since it's protected)
                var idProperty = typeof(Donation).BaseType?.GetProperty("Id");
                idProperty?.SetValue(donation, data.Id);

                // Set DateCreated using reflection (since it's protected)
                var dateCreatedProperty = typeof(Donation).BaseType?.GetProperty("DateCreated");
                dateCreatedProperty?.SetValue(donation, data.DateCreated);

                if (!string.IsNullOrWhiteSpace(data.SoftCreditName?.ToString()))
                {
                    donation.SetSoftCredit(data.SoftCreditName.ToString());
                }

                donations.Add(donation);
            }
            catch (Exception ex)
            {
                // Log error but continue with other donations
                Console.WriteLine($"Error mapping donation {data.Id}: {ex.Message}");
            }
        }

        return donations;
    }

    private static PaymentMethod ParsePaymentMethod(string? paymentMethod)
    {
        return paymentMethod?.ToLowerInvariant() switch
        {
            "cash" => PaymentMethod.Cash,
            "check" => PaymentMethod.Check,
            "credit card" or "creditcard" => PaymentMethod.CreditCard,
            "debit card" or "debitcard" => PaymentMethod.DebitCard,
            "bank transfer" or "banktransfer" => PaymentMethod.BankTransfer,
            "paypal" => PaymentMethod.PayPal,
            "cryptocurrency" or "crypto" => PaymentMethod.Cryptocurrency,
            "in-kind" or "inkind" => PaymentMethod.InKind,
            _ => PaymentMethod.Other
        };
    }

    private static GiftType ParseGiftType(string? giftType)
    {
        return giftType?.ToLowerInvariant() switch
        {
            "one-time" or "onetime" => GiftType.OneTime,
            "recurring" => GiftType.Recurring,
            "pledge" => GiftType.Pledge,
            "in-kind" or "inkind" => GiftType.InKind,
            "memorial" => GiftType.Memorial,
            "honor" => GiftType.Honor,
            _ => GiftType.Other
        };
    }
}