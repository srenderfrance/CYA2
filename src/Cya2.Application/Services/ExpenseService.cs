using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Cya2.Core.Services;
using Cya2.Core.Utilities;
using Cya2.Application.Models;

namespace Cya2.Application.Services;

/// <summary>
/// Expense management service implementation for clean architecture
/// </summary>
public class ExpenseService : IExpenseService
{
    private readonly ILogger<ExpenseService> _logger;
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IAccountFactDataService _accountFactDataService;
    private readonly IAccountingTransactionProcessor _accountingTransactionProcessor;
    private readonly IUserAuthorizationService _authorizationService;

    public ExpenseService(
        ILogger<ExpenseService> logger,
        IUserAccountContextService userAccountContextService,
        IAccountFactDataService accountFactDataService,
        IAccountingTransactionProcessor accountingTransactionProcessor,
        IUserAuthorizationService authorizationService)
    {
        _logger = logger;
        _userAccountContextService = userAccountContextService;
        _accountFactDataService = accountFactDataService;
        _accountingTransactionProcessor = accountingTransactionProcessor;
        _authorizationService = authorizationService;
    }

    public async Task<List<AccountOptionDto>> GetUserAccountsAsync(string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var mapped = (context?.Accounts ?? new List<UserAccountContextAccount>())
                .Select(MapToAccountOptionDto)
                .ToList();
            return mapped;
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
            var authorization = string.IsNullOrWhiteSpace(accountName)
                ? await _authorizationService.ValidateUserAsync(userId)
                : await _authorizationService.ValidateAccountAccessAsync(userId, accountName);
            if (!authorization.IsAuthorized)
                return new ExpenseDataDto();

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

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange, userId);

            var categorized = _accountingTransactionProcessor.Process(selectedAccount, accountingData);

            var expenseTransactions = categorized.ExpenseTransactions.Select(MapToExpenseTransactionDto).ToList();
            var transferTransactions = categorized.TransferTransactions.Select(MapToExpenseTransactionDto).ToList();

            var result = new ExpenseDataDto
            {
                UserAccounts = contextAccounts.Select(MapToAccountOptionDto).ToList(),
                SelectedAccount = selectedAccount.Fund ?? string.Empty,
                ExpenseTransactions = expenseTransactions,
                TransferTransactions = transferTransactions,
                ExpenseTotal = categorized.ExpenseTotal,
                TransferTotal = categorized.TransferTotal,
                DateRangeStart = dateRange.StartDate,
                DateRangeEnd = dateRange.EndDate
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting expense data for account={AccountName} user={UserId} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}",
                accountName,
                userId,
                dateRange.StartDate,
                dateRange.EndDate);
            var fallbackAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            return new ExpenseDataDto
            {
                UserAccounts = fallbackAccounts
            };
        }
    }

    private async Task<List<AccountingRecord>> LoadAccountingDataAsync(
        UserAccountContextAccount account,
        DateRange dateRange,
        string userId)
        => await _accountFactDataService.GetAccountingDataAsync(
            account,
            userId,
            dateRange.StartDate,
            dateRange.EndDate);

    public async Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var selectedAccount = context == null ? null : _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null) return new List<ExpenseTransactionDto>();

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange, userId);
            var categorized = _accountingTransactionProcessor.Process(selectedAccount, accountingData);

            return categorized.ExpenseTransactions.Select(MapToExpenseTransactionDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense transactions for user: {UserId}", userId);
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

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange, userId);
            var categorized = _accountingTransactionProcessor.Process(selectedAccount, accountingData);

            return categorized.TransferTransactions.Select(MapToExpenseTransactionDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transfer transactions for user: {UserId}", userId);
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

            var accountingData = await LoadAccountingDataAsync(selectedAccount, dateRange, userId);
            var categorized = _accountingTransactionProcessor.Process(selectedAccount, accountingData);

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
            _logger.LogError(ex, "Error getting expense summary for user: {UserId}", userId);
            return new ExpenseSummaryDto
            {
                AccountName = accountName,
                PeriodStart = dateRange.StartDate,
                PeriodEnd = dateRange.EndDate
            };
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
            InternName = account.InternName ?? (InternAccountUtility.TryGetInternDesignationName(account.Fund, out var internName)
                ? internName
                : string.Empty),
            DisplayName = string.IsNullOrWhiteSpace(account.InternName)
                ? InternAccountUtility.GetAccountSelectorDisplayName(account.Fund)
                : $"{account.InternName} (Intern)",
            AccountingClass = account.AccountingClass ?? string.Empty,
            AccountNumber = account.AccountNumber ?? string.Empty,
            Overhead = Convert.ToDecimal(account.Overhead)
        };
    }
}