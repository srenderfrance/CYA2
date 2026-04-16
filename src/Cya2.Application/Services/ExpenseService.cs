using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Cya2.Core.Services;

namespace Cya2.Application.Services;

/// <summary>
/// Expense management service implementation for clean architecture
/// </summary>
public class ExpenseService : IExpenseService
{
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly ILogger<ExpenseService> _logger;
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly ISessionExpenseDataCacheService _expenseCache;
    private readonly ExpenseClassificationService _classifier;

    public ExpenseService(
        IExpenseReadRepository expenseReadRepository,
        ILogger<ExpenseService> logger,
        IUserAccountContextService userAccountContextService,
        ISessionExpenseDataCacheService expenseCache)
    {
        _expenseReadRepository = expenseReadRepository;
        _logger = logger;
        _userAccountContextService = userAccountContextService;
        _expenseCache = expenseCache;
        _classifier = new ExpenseClassificationService();
    }

    public async Task<List<AccountOptionDto>> GetUserAccountsAsync(string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            return (context?.Accounts ?? new List<UserAccountContextAccount>())
                .Select(MapToAccountOptionDto)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user accounts for userId: {UserId}", userId);
            return new List<AccountOptionDto>();
        }
    }

    public async Task<ExpenseDataDto> GetExpenseDataAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var contextAccounts = context?.Accounts ?? new List<UserAccountContextAccount>();

            if (context == null || !contextAccounts.Any())
            {
                _logger.LogWarning("No accounts found for user: {UserId}", userId);
                return new ExpenseDataDto
                {
                    UserAccounts = contextAccounts.Select(MapToAccountOptionDto).ToList()
                };
            }

            var selectedAccount = _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null)
            {
                return new ExpenseDataDto { UserAccounts = contextAccounts.Select(MapToAccountOptionDto).ToList() };
            }

            if (_expenseCache.TryGetExpenseData(userId, selectedAccount.Fund ?? string.Empty, dateRange.StartDate, dateRange.EndDate, out var cached))
            {
                cached.UserAccounts = contextAccounts.Select(MapToAccountOptionDto).ToList();
                cached.SelectedAccount = selectedAccount.Fund ?? string.Empty;
                return cached;
            }

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);

            var categorized = _classifier.Categorize(accountingData);
            _logger.LogInformation("[ExpenseService] Data for account {Account}: expenses={Expenses}, transfers={Transfers}", selectedAccount.Fund, categorized.ExpenseTransactions.Count, categorized.TransferTransactions.Count);

            var expenseTransactions = categorized.ExpenseTransactions.Select(MapToExpenseTransactionDto).ToList();
            var transferTransactions = categorized.TransferTransactions.Select(MapToExpenseTransactionDto).ToList();

            var result = new ExpenseDataDto
            {
                UserAccounts = contextAccounts.Select(MapToAccountOptionDto).ToList(),
                SelectedAccount = selectedAccount.Fund,
                ExpenseTransactions = expenseTransactions,
                TransferTransactions = transferTransactions,
                ExpenseTotal = categorized.ExpenseTotal,
                TransferTotal = categorized.TransferTotal,
                DateRangeStart = dateRange.StartDate,
                DateRangeEnd = dateRange.EndDate
            };

            _expenseCache.SetExpenseData(userId, selectedAccount.Fund ?? string.Empty, dateRange.StartDate, dateRange.EndDate, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense data for account: {AccountName}", accountName);
            var fallbackAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            return new ExpenseDataDto
            {
                UserAccounts = fallbackAccounts
            };
        }
    }

    public async Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var selectedAccount = context == null ? null : _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null) return new List<ExpenseTransactionDto>();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = _classifier.Categorize(accountingData);

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
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var selectedAccount = context == null ? null : _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null) return new List<ExpenseTransactionDto>();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = _classifier.Categorize(accountingData);

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
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var selectedAccount = context == null ? null : _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null)
            {
                return new ExpenseSummaryDto
                {
                    AccountName = accountName,
                    PeriodStart = dateRange.StartDate,
                    PeriodEnd = dateRange.EndDate
                };
            }

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange);
            var categorized = _classifier.Categorize(accountingData);

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

    private async Task<List<AccountingRecord>> LoadAccountingDataAsync(UserAccountContextAccount account, DateRange dateRange)
    {
        try
        {
            var result = await _expenseReadRepository.GetAccountingDataByClassAndDateAsync(
                account.AccountingClass,
                dateRange.StartDate,
                dateRange.EndDate);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accounting data for account: {AccountName}", account.Fund);
            return new List<AccountingRecord>();
        }
    }

    private static ExpenseTransactionDto MapToExpenseTransactionDto(AccountingRecord model)
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

    private static AccountOptionDto MapToAccountOptionDto(UserAccountContextAccount account)
    {
        return new AccountOptionDto
        {
            AccountId = account.AccountId,
            Fund = account.Fund ?? string.Empty,
            AccountingClass = account.AccountingClass ?? string.Empty,
            AccountNumber = account.AccountNumber ?? string.Empty,
            Overhead = Convert.ToDecimal(account.Overhead)
        };
    }
}