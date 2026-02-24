using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;
using ModelsLibrary;
using Cya2.Application.Utilities;

namespace Cya2.Application.Services;

/// <summary>
/// Expense management service implementation for clean architecture
/// </summary>
public class ExpenseService : IExpenseService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(IDataAccess dataAccess, IConfiguration configuration, ILogger<ExpenseService> logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<Account>> GetUserAccountsAsync(string userId, bool isAdminOrViewer = false)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("GetUserAccountsAsync called with empty userId");
                return new List<Account>();
            }

            int userIdInt = 0;
            string authLevel = string.Empty;

            if (!isAdminOrViewer)
            {
                if (int.TryParse(userId, out var parsed) && parsed > 0)
                {
                    userIdInt = parsed;
                    const string authByIdSql = "SELECT AuthLevel FROM Users WHERE Id = @UserId";
                    var authRows = await _dataAccess.LoadData<dynamic, object>(authByIdSql, new { UserId = userIdInt }, GetConnectionString());
                    authLevel = authRows?.FirstOrDefault()?.AuthLevel as string ?? string.Empty;
                }
                else
                {
                    const string userByEmailSql = "SELECT Id, AuthLevel FROM Users WHERE Email = @Email";
                    var userRows = await _dataAccess.LoadData<dynamic, object>(userByEmailSql, new { Email = userId }, GetConnectionString());
                    var row = userRows?.FirstOrDefault();
                    userIdInt = row?.Id ?? 0;
                    authLevel = row?.AuthLevel ?? string.Empty;
                    _logger.LogInformation("[ExpenseService] Lookup user by email {Email} -> Id={UserId} Auth={AuthLevel}", userId, userIdInt, authLevel);
                }

                if (userIdInt == 0)
                {
                    _logger.LogWarning("Could not resolve user ID for identifier: {UserId}", userId);
                    return new List<Account>();
                }

                isAdminOrViewer = string.Equals(authLevel, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(authLevel, "Viewer", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                _ = int.TryParse(userId, out userIdInt);
            }

            string sql;
            object parameters;
            if (isAdminOrViewer)
            {
                sql = @"SELECT AccountId, Fund, AccountingClass, CreatedAt, Overhead, AccountNumber, SoftCredit, BalanceAdjustment
                        FROM Accounts
                        ORDER BY Fund";
                parameters = new { };
            }
            else
            {
                sql = @"SELECT a.AccountId, a.Fund, a.AccountingClass, a.CreatedAt, a.Overhead,
                               a.AccountNumber, a.SoftCredit, a.BalanceAdjustment
                        FROM Accounts a
                        INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
                        WHERE au.UserId = @UserId
                        ORDER BY a.Fund";
                parameters = new { UserId = userIdInt };
            }

            var accounts = await _dataAccess.LoadData<Account, object>(sql, parameters, GetConnectionString());
            _logger.LogInformation("[ExpenseService] Accounts for user {UserId} (admin/viewer={IsAdminOrViewer}): {Count}", userIdInt, isAdminOrViewer, accounts?.Count() ?? 0);
            
            return accounts?.ToList() ?? new List<Account>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user accounts for userId: {UserId}", userId);
            return new List<Account>();
        }
    }

    public async Task<ExpenseDataDto> GetExpenseDataAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var userAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            if (!userAccounts.Any())
            {
                _logger.LogWarning("No accounts found for user: {UserId}", userId);
                return new ExpenseDataDto { UserAccounts = userAccounts };
            }

            var selectedAccount = userAccounts.FirstOrDefault(a => a.Fund == accountName) ?? userAccounts.First();
            var resolvedAccountName = selectedAccount.Fund;

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            
            var categorized = TransactionCategorizer.CategorizeTransactions(accountingData);
            _logger.LogInformation("[ExpenseService] Data for account {Account}: expenses={Expenses}, transfers={Transfers}", resolvedAccountName, categorized.ExpenseTransactions.Count, categorized.TransferTransactions.Count);

            var expenseTransactions = categorized.ExpenseTransactions.Select(MapToExpenseTransactionDto).ToList();
            var transferTransactions = categorized.TransferTransactions.Select(MapToExpenseTransactionDto).ToList();

            return new ExpenseDataDto
            {
                UserAccounts = userAccounts,
                SelectedAccount = resolvedAccountName,
                ExpenseTransactions = expenseTransactions,
                TransferTransactions = transferTransactions,
                ExpenseTotal = categorized.ExpenseTotal,
                TransferTotal = categorized.TransferTotal,
                DateRangeStart = dateRange.StartDate,
                DateRangeEnd = dateRange.EndDate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense data for account: {AccountName}", accountName);
            return new ExpenseDataDto { UserAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer) };
        }
    }

    public async Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var userAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            if (!userAccounts.Any()) return new List<ExpenseTransactionDto>();

            var selectedAccount = userAccounts.FirstOrDefault(a => a.Fund == accountName) ?? userAccounts.First();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = TransactionCategorizer.CategorizeTransactions(accountingData);

            return categorized.ExpenseTransactions.Select(MapToExpenseTransactionDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense transactions for account: {AccountName}", accountName);
            return new List<ExpenseTransactionDto>();
        }
    }

    public async Task<List<ExpenseTransactionDto>> GetTransferTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var userAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            if (!userAccounts.Any()) return new List<ExpenseTransactionDto>();

            var selectedAccount = userAccounts.FirstOrDefault(a => a.Fund == accountName) ?? userAccounts.First();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = TransactionCategorizer.CategorizeTransactions(accountingData);

            return categorized.TransferTransactions.Select(MapToExpenseTransactionDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transfer transactions for account: {AccountName}", accountName);
            return new List<ExpenseTransactionDto>();
        }
    }

    public async Task<ExpenseSummaryDto> GetExpenseSummaryAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var userAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            if (!userAccounts.Any())
            {
                return new ExpenseSummaryDto
                {
                    AccountName = accountName,
                    PeriodStart = dateRange.StartDate,
                    PeriodEnd = dateRange.EndDate
                };
            }

            var selectedAccount = userAccounts.FirstOrDefault(a => a.Fund == accountName) ?? userAccounts.First();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = TransactionCategorizer.CategorizeTransactions(accountingData);

            return new ExpenseSummaryDto
            {
                TotalExpenses = categorized.ExpenseTotal,
                TotalTransfers = categorized.TransferTotal,
                ExpenseTransactionCount = categorized.ExpenseTransactions.Count,
                TransferTransactionCount = categorized.TransferTransactions.Count,
                PeriodStart = dateRange.StartDate,
                PeriodEnd = dateRange.EndDate,
                AccountName = selectedAccount.Fund
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense summary for account: {AccountName}", accountName);
            return new ExpenseSummaryDto
            {
                AccountName = accountName,
                PeriodStart = dateRange.StartDate,
                PeriodEnd = dateRange.EndDate
            };
        }
    }

    private async Task<List<AccountingDataModel>> LoadAccountingDataAsync(Account account, DateRange dateRange)
    {
        try
        {
            const string sql = @"
                SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
                FROM AccountingData
                WHERE AccountingClass = @AccountClass
                  AND Date >= @StartDate 
                  AND Date <= @EndDate
                ORDER BY Date DESC";

            var result = await _dataAccess.LoadData<AccountingDataModel, object>(sql, new { 
                AccountClass = account.AccountingClass,
                StartDate = dateRange.StartDate,
                EndDate = dateRange.EndDate
            }, GetConnectionString());
            
            return result?.ToList() ?? new List<AccountingDataModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accounting data for account: {AccountName}", account.Fund);
            return new List<AccountingDataModel>();
        }
    }

    private static ExpenseTransactionDto MapToExpenseTransactionDto(AccountingDataModel model)
    {
        return new ExpenseTransactionDto
        {
            Id = model.Id,
            Date = model.Date,
            Num = model.Num,
            Amount = Convert.ToDecimal(model.Amount),
            Account = model.Account,
            Type = model.Type,
            AccountingClass = model.AccountingClass,
            AccountNumber = model.AccountNumber
        };
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("default") ?? string.Empty;
    }
}