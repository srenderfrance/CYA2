using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Infrastructure.Data.Repositories;

public class DonationRepository : BaseRepository, IDonationRepository
{
    public DonationRepository(IConfiguration configuration, ILogger<DonationRepository> logger) 
        : base(configuration, logger)
    {
    }

    public async Task<List<Donation>> GetByDonorNameAsync(string donorName)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE AccountName = @DonorName
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { DonorName = donorName });
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<List<Donation>> GetByAccountFundAsync(string accountFund)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE Fund = @AccountFund
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { AccountFund = accountFund });
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<List<Donation>> GetByDateRangeAsync(DateRange dateRange)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE Date >= @StartDate AND Date <= @EndDate
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { 
                StartDate = dateRange.StartDate, 
                EndDate = dateRange.EndDate 
            });
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<List<Donation>> GetByDonorAndAccountAsync(string donorName, string accountFund)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE AccountName = @DonorName AND Fund = @AccountFund
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { 
                DonorName = donorName, 
                AccountFund = accountFund 
            });
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<List<Donation>> GetAllAsync()
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql);
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<Donation> AddAsync(Donation donation)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                INSERT INTO Donations (Date, AccountName, PaymentMethod, GiftType, Amount, Fund, 
                                     SoftCreditName, DateCreated, IsAnonymous)
                VALUES (@Date, @DonorName, @PaymentMethod, @GiftType, @Amount, @AccountFund, 
                        @SoftCreditName, @DateCreated, @IsAnonymous);
                SELECT LAST_INSERT_ID();";

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

            var newId = await connection.QueryFirstAsync<int>(sql, parameters);
            
            // Return a new donation with the generated ID
            var newDonation = new Donation(
                donation.Amount, 
                donation.Date, 
                donation.DonorName, 
                donation.AccountFund,
                donation.PaymentMethod, 
                donation.GiftType, 
                donation.IsAnonymous
            );

            // Use reflection to set the ID (since it's protected)
            var idProperty = typeof(Donation).BaseType?.GetProperty("Id");
            idProperty?.SetValue(newDonation, newId);

            _logger.LogInformation("Created donation {DonationId} for {DonorName}", newId, donation.DonorName);
            return newDonation;
        });
    }

    public async Task<Donation> UpdateAsync(Donation donation)
    {
        return await ExecuteWithRetryAsync(async connection =>
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

            var affectedRows = await connection.ExecuteAsync(sql, parameters);
            
            if (affectedRows == 0)
                throw new ArgumentException($"Donation with ID {donation.Id} not found");

            _logger.LogInformation("Updated donation {DonationId}", donation.Id);
            return donation;
        });
    }

    public async Task DeleteAsync(int id)
    {
        await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = "DELETE FROM Donations WHERE Id = @Id";
            
            var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });
            
            if (affectedRows == 0)
                throw new ArgumentException($"Donation with ID {id} not found");

            _logger.LogInformation("Deleted donation {DonationId}", id);
        });
    }

    public async Task<decimal> GetTotalByAccountAsync(string accountFund, DateRange dateRange)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT COALESCE(SUM(Amount), 0)
                FROM Donations 
                WHERE Fund = @AccountFund 
                  AND Date >= @StartDate AND Date <= @EndDate";

            return await connection.QueryFirstAsync<decimal>(sql, new { 
                AccountFund = accountFund,
                StartDate = dateRange.StartDate, 
                EndDate = dateRange.EndDate 
            });
        });
    }

    public async Task<List<Donation>> GetRecentDonationsAsync(int days = 30)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            var cutoffDate = DateTime.Today.AddDays(-days);
            
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE Date >= @CutoffDate
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { CutoffDate = cutoffDate });
            return MapToDomainEntities(donationData);
        });
    }

    // Add missing methods from interface
    public async Task<Donation?> GetByIdAsync(int id)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE Id = @Id";

            var data = await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });
            
            if (data == null) return null;

            var donations = MapToDomainEntities(new[] { data });
            return donations.FirstOrDefault();
        });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = "SELECT COUNT(1) FROM Donations WHERE Id = @Id";
            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { Id = id });
            return count > 0;
        });
    }

    public async Task<List<Donation>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                       Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
                FROM Donations 
                WHERE Date >= @StartDate AND Date <= @EndDate
                ORDER BY Date DESC";

            var donationData = await connection.QueryAsync(sql, new { 
                StartDate = startDate, 
                EndDate = endDate 
            });
            return MapToDomainEntities(donationData);
        });
    }

    public async Task<List<Donation>> GetByDonorAsync(string donorName)
    {
        return await GetByDonorNameAsync(donorName);
    }

    public async Task<decimal> GetTotalByAccountFundAsync(string accountFund, DateTime startDate, DateTime endDate)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT COALESCE(SUM(Amount), 0)
                FROM Donations 
                WHERE Fund = @AccountFund 
                  AND Date >= @StartDate 
                  AND Date <= @EndDate";

            var total = await connection.QueryFirstOrDefaultAsync<decimal>(sql, 
                new { AccountFund = accountFund, StartDate = startDate, EndDate = endDate });

            return total;
        });
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