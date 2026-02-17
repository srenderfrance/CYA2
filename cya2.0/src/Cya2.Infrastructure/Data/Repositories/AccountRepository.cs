using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Enums;

namespace Cya2.Infrastructure.Data.Repositories;

public class AccountRepository : BaseRepository, IAccountRepository
{
    public AccountRepository(IConfiguration configuration, ILogger<AccountRepository> logger) 
        : base(configuration, logger)
    {
    }

    public async Task<Account?> GetByFundCodeAsync(string fundCode)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            // Get account from Accounts table
            const string accountSql = @"
                SELECT AccountId as Id, Fund as FundCode, Fund as Name
                FROM Accounts 
                WHERE Fund = @FundCode
                LIMIT 1";

            var accountData = await connection.QueryFirstOrDefaultAsync(accountSql, new { FundCode = fundCode });
            
            if (accountData == null)
                return null;

            var account = new Account(
                accountData.FundCode?.ToString() ?? fundCode,
                accountData.Name?.ToString() ?? fundCode,
                AccountType.Primary // Default type, could be enhanced
            );

            // Set ID using reflection
            var idProperty = typeof(Account).BaseType?.GetProperty("Id");
            idProperty?.SetValue(account, accountData.Id);

            // Load sub-accounts
            await LoadSubAccounts(connection, account);

            // Load donations for this account
            await LoadDonations(connection, account, fundCode);

            // Load accounting entries
            await LoadAccountingEntries(connection, account, fundCode);

            return account;
        });
    }

    public async Task<List<Account>> GetAllAsync()
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT DISTINCT AccountId as Id, Fund as FundCode, Fund as Name
                FROM Accounts 
                ORDER BY Fund";

            var accountsData = await connection.QueryAsync(sql);
            var accounts = new List<Account>();

            foreach (var accountData in accountsData)
            {
                var account = await GetByFundCodeAsync(accountData.FundCode?.ToString() ?? "");
                if (account != null)
                    accounts.Add(account);
            }

            return accounts;
        });
    }

    public async Task<List<Account>> GetByUserIdAsync(int userId)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            // Get accounts the user has access to via AccountsUsers table
            const string sql = @"
                SELECT DISTINCT a.AccountId as Id, a.Fund as FundCode, a.Fund as Name
                FROM Accounts a
                INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
                WHERE au.UserId = @UserId
                ORDER BY a.Fund";

            var accountsData = await connection.QueryAsync(sql, new { UserId = userId });
            var accounts = new List<Account>();

            foreach (var accountData in accountsData)
            {
                var account = await GetByFundCodeAsync(accountData.FundCode?.ToString() ?? "");
                if (account != null)
                    accounts.Add(account);
            }

            return accounts;
        });
    }

    public async Task<Account> AddAsync(Account account)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                INSERT INTO Accounts (Fund)
                VALUES (@FundCode);
                SELECT LAST_INSERT_ID();";

            var newId = await connection.QueryFirstAsync<int>(sql, new { FundCode = account.FundCode });
            
            // Set ID using reflection
            var idProperty = typeof(Account).BaseType?.GetProperty("Id");
            idProperty?.SetValue(account, newId);

            _logger.LogInformation("Created account {AccountId} with fund code {FundCode}", newId, account.FundCode);
            return account;
        });
    }

    public async Task<Account> UpdateAsync(Account account)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                UPDATE Accounts 
                SET Fund = @FundCode
                WHERE AccountId = @Id";

            var affectedRows = await connection.ExecuteAsync(sql, new { 
                Id = account.Id,
                FundCode = account.FundCode 
            });
            
            if (affectedRows == 0)
                throw new ArgumentException($"Account with ID {account.Id} not found");

            _logger.LogInformation("Updated account {AccountId}", account.Id);
            return account;
        });
    }

    public async Task DeleteAsync(int id)
    {
        await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = "DELETE FROM Accounts WHERE AccountId = @Id";
            
            var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });
            
            if (affectedRows == 0)
                throw new ArgumentException($"Account with ID {id} not found");

            _logger.LogInformation("Deleted account {AccountId}", id);
        });
    }

    public async Task<bool> ExistsAsync(string fundCode)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT COUNT(1) 
                FROM Accounts 
                WHERE Fund = @FundCode";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { FundCode = fundCode });
            return count > 0;
        });
    }

    private async Task LoadSubAccounts(System.Data.IDbConnection connection, Account account)
    {
        const string sql = @"
            SELECT Id, AccountId, SubFund, Kind
            FROM SubAccounts 
            WHERE AccountId = @AccountId";

        var subAccountsData = await connection.QueryAsync(sql, new { AccountId = account.Id });

        foreach (var subData in subAccountsData)
        {
            var subAccountType = ParseSubAccountType(subData.Kind?.ToString());
            var subAccount = new SubAccount(
                subData.SubFund?.ToString() ?? "",
                subAccountType,
                account
            );

            // Set ID using reflection
            var idProperty = typeof(SubAccount).BaseType?.GetProperty("Id");
            idProperty?.SetValue(subAccount, subData.Id);

            // Add to account's collection using reflection (since it's private)
            var subAccountsProperty = typeof(Account).GetProperty("SubAccounts");
            var subAccountsList = (List<SubAccount>)subAccountsProperty?.GetValue(account)!;
            subAccountsList.Add(subAccount);
        }
    }

    private async Task LoadDonations(System.Data.IDbConnection connection, Account account, string fundCode)
    {
        const string sql = @"
            SELECT Id, Date, AccountName as DonorName, PaymentMethod, GiftType, 
                   Amount, Fund as AccountFund, SoftCreditName, DateCreated, IsAnonymous
            FROM Donations 
            WHERE Fund = @FundCode
            ORDER BY Date DESC";

        var donationsData = await connection.QueryAsync(sql, new { FundCode = fundCode });

        foreach (var donationData in donationsData)
        {
            try
            {
                var paymentMethod = ParsePaymentMethod(donationData.PaymentMethod?.ToString());
                var giftType = ParseGiftType(donationData.GiftType?.ToString());
                
                var donation = new Donation(
                    Convert.ToDecimal(donationData.Amount), 
                    donationData.Date, 
                    donationData.DonorName?.ToString() ?? "",
                    donationData.AccountFund?.ToString() ?? "",
                    paymentMethod,
                    giftType,
                    donationData.IsAnonymous
                );

                // Set ID and DateCreated using reflection
                var idProperty = typeof(Donation).BaseType?.GetProperty("Id");
                idProperty?.SetValue(donation, donationData.Id);

                var dateCreatedProperty = typeof(Donation).BaseType?.GetProperty("DateCreated");
                dateCreatedProperty?.SetValue(donation, donationData.DateCreated);

                donation.AssignToAccount(account);

                // Add to account's donations collection using reflection
                var donationsProperty = typeof(Account).GetProperty("Donations");
                var donationsList = (List<Donation>)donationsProperty?.GetValue(account)!;
                donationsList.Add(donation);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading donation {DonationId} for account {AccountId}", 
                    donationData.Id, account.Id);
            }
        }
    }

    private async Task LoadAccountingEntries(System.Data.IDbConnection connection, Account account, string fundCode)
    {
        const string sql = @"
            SELECT Id, Date, Amount, Description, Fund as AccountFund, Category, DateCreated
            FROM Accounting 
            WHERE Fund = @FundCode
            ORDER BY Date DESC";

        var entriesData = await connection.QueryAsync(sql, new { FundCode = fundCode });

        foreach (var entryData in entriesData)
        {
            try
            {
                var entry = new AccountingEntry(
                    entryData.Date,
                    Convert.ToDecimal(entryData.Amount),
                    entryData.Description?.ToString() ?? "",
                    entryData.AccountFund?.ToString() ?? "",
                    entryData.Category?.ToString() ?? ""
                );

                // Set ID and DateCreated using reflection
                var idProperty = typeof(AccountingEntry).BaseType?.GetProperty("Id");
                idProperty?.SetValue(entry, entryData.Id);

                var dateCreatedProperty = typeof(AccountingEntry).BaseType?.GetProperty("DateCreated");
                dateCreatedProperty?.SetValue(entry, entryData.DateCreated);

                entry.AssignToAccount(account);

                // Add to account's entries collection using reflection
                var entriesProperty = typeof(Account).GetProperty("AccountingEntries");
                var entriesList = (List<AccountingEntry>)entriesProperty?.GetValue(account)!;
                entriesList.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading accounting entry {EntryId} for account {AccountId}", 
                    entryData.Id, account.Id);
            }
        }
    }

    private static SubAccountType ParseSubAccountType(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "separate" => SubAccountType.Separate,
            "consolidated" => SubAccountType.Consolidated,
            "project" => SubAccountType.Project,
            "restricted" => SubAccountType.Restricted,
            _ => SubAccountType.Separate
        };
    }

    private static Core.Enums.PaymentMethod ParsePaymentMethod(string? paymentMethod)
    {
        return paymentMethod?.ToLowerInvariant() switch
        {
            "cash" => Core.Enums.PaymentMethod.Cash,
            "check" => Core.Enums.PaymentMethod.Check,
            "credit card" or "creditcard" => Core.Enums.PaymentMethod.CreditCard,
            "debit card" or "debitcard" => Core.Enums.PaymentMethod.DebitCard,
            "bank transfer" or "banktransfer" => Core.Enums.PaymentMethod.BankTransfer,
            "paypal" => Core.Enums.PaymentMethod.PayPal,
            "cryptocurrency" or "crypto" => Core.Enums.PaymentMethod.Cryptocurrency,
            "in-kind" or "inkind" => Core.Enums.PaymentMethod.InKind,
            _ => Core.Enums.PaymentMethod.Other
        };
    }

    private static Core.Enums.GiftType ParseGiftType(string? giftType)
    {
        return giftType?.ToLowerInvariant() switch
        {
            "one-time" or "onetime" => Core.Enums.GiftType.OneTime,
            "recurring" => Core.Enums.GiftType.Recurring,
            "pledge" => Core.Enums.GiftType.Pledge,
            "in-kind" or "inkind" => Core.Enums.GiftType.InKind,
            "memorial" => Core.Enums.GiftType.Memorial,
            "honor" => Core.Enums.GiftType.Honor,
            _ => Core.Enums.GiftType.Other
        };
    }
}