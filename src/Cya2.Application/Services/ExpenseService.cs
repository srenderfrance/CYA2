using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Cya2.Core.Services;
using Cya2.Application.Models;
using System.Diagnostics;

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
    private readonly IAccountSnapshotCache _accountSnapshotCache;
    private readonly IAccountSnapshotLoader _accountSnapshotLoader;
    private readonly ExpenseClassificationService _classifier;

    public ExpenseService(
        IExpenseReadRepository expenseReadRepository,
        ILogger<ExpenseService> logger,
        IUserAccountContextService userAccountContextService,
        ISessionExpenseDataCacheService expenseCache,
        IAccountSnapshotCache accountSnapshotCache,
        IAccountSnapshotLoader accountSnapshotLoader)
    {
        _expenseReadRepository = expenseReadRepository;
        _logger = logger;
        _userAccountContextService = userAccountContextService;
        _expenseCache = expenseCache;
        _accountSnapshotCache = accountSnapshotCache;
        _accountSnapshotLoader = accountSnapshotLoader;
        _classifier = new ExpenseClassificationService();
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
        var sw = Stopwatch.StartNew();
        try
        {
            if (!string.IsNullOrWhiteSpace(accountName) &&
                _expenseCache.TryGetExpenseData(userId, accountName, dateRange.StartDate, dateRange.EndDate, out var directCached))
            {
                directCached.SelectedAccount = accountName;
                _logger.LogInformation(
                    "Expense data source=cache-direct user={UserId} account={Account} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} expenses={ExpenseCount} transfers={TransferCount} elapsedMs={ElapsedMs}",
                    userId,
                    directCached.SelectedAccount,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    directCached.ExpenseTransactions?.Count ?? 0,
                    directCached.TransferTransactions?.Count ?? 0,
                    sw.ElapsedMilliseconds);
                return directCached;
            }

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
                _logger.LogInformation(
                    "Expense data source=cache user={UserId} account={Account} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} expenses={ExpenseCount} transfers={TransferCount} elapsedMs={ElapsedMs}",
                    userId,
                    cached.SelectedAccount,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    cached.ExpenseTransactions?.Count ?? 0,
                    cached.TransferTransactions?.Count ?? 0,
                    sw.ElapsedMilliseconds);
                return cached;
            }

            var useSnapshot = CanUseAccountSnapshot(dateRange);
            var accountingData = useSnapshot
                ? await LoadAccountingDataFromSnapshotAsync(selectedAccount, dateRange, sw)
                : await LoadAccountingDataAsync(selectedAccount, dateRange);

            var categorized = _classifier.Categorize(accountingData);
            _logger.LogInformation("[ExpenseService] Data for account {Account}: expenses={Expenses}, transfers={Transfers}", selectedAccount.Fund, categorized.ExpenseTransactions.Count, categorized.TransferTransactions.Count);

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

            _expenseCache.SetExpenseData(userId, selectedAccount.Fund ?? string.Empty, dateRange.StartDate, dateRange.EndDate, result);

            _logger.LogInformation(
                "Expense data source={Source} user={UserId} account={Account} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} expenses={ExpenseCount} transfers={TransferCount} elapsedMs={ElapsedMs}",
                useSnapshot ? "snapshot" : "repository",
                userId,
                result.SelectedAccount,
                dateRange.StartDate,
                dateRange.EndDate,
                result.ExpenseTransactions?.Count ?? 0,
                result.TransferTransactions?.Count ?? 0,
                sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting expense data for account={AccountName} user={UserId} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} elapsedMs={ElapsedMs}",
                accountName,
                userId,
                dateRange.StartDate,
                dateRange.EndDate,
                sw.ElapsedMilliseconds);
            var fallbackAccounts = await GetUserAccountsAsync(userId, isAdminOrViewer);
            return new ExpenseDataDto
            {
                UserAccounts = fallbackAccounts
            };
        }
    }

    private async Task<List<AccountingRecord>> LoadAccountingDataFromSnapshotAsync(
        UserAccountContextAccount account,
        DateRange dateRange,
        Stopwatch stopwatch)
    {
        var snapshotQueryRange = GetSnapshotQueryRange();
        var key = new AccountSnapshotKey(account.AccountId, account.Fund, 0).Normalize();
        var wasCached = _accountSnapshotCache.TryGet(key, out var snapshot);

        if (!wasCached)
        {
            snapshot = await _accountSnapshotCache.GetOrCreateAsync(
                key,
                cancellationToken => _accountSnapshotLoader.LoadAsync(account, snapshotQueryRange, key, cancellationToken),
                CancellationToken.None);
        }

        var accounting = snapshot.Accounting
            .Where(record => record.Date.Date >= dateRange.StartDate.Date && record.Date.Date <= dateRange.EndDate.Date)
            .Select(MapToAccountingRecord)
            .ToList();

        _logger.LogInformation(
            "Expense data source={Source} account={Account} requestedRange={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} queriedRange={QueriedStart:yyyy-MM-dd}..{QueriedEnd:yyyy-MM-dd} snapshotCreatedUtc={SnapshotCreatedUtc:o} accountingRows={AccountingCount} elapsedMs={ElapsedMs}",
            wasCached ? "snapshot-cache" : "snapshot-load",
            account.Fund,
            dateRange.StartDate,
            dateRange.EndDate,
            snapshotQueryRange.StartDate,
            snapshotQueryRange.EndDate,
            snapshot.CreatedUtc,
            accounting.Count,
            stopwatch.ElapsedMilliseconds);

        return accounting;
    }

    private static AccountingRecord MapToAccountingRecord(AccountingSnapshot snapshot)
    {
        return new AccountingRecord
        {
            Id = snapshot.Id,
            AccountingClass = snapshot.AccountingClass,
            Date = snapshot.Date,
            Num = snapshot.Num,
            Amount = snapshot.Amount,
            AccountNumber = snapshot.AccountNumber,
            Account = snapshot.Account,
            Type = snapshot.Type,
            DateCreated = snapshot.DateCreated
        };
    }

    private static bool CanUseAccountSnapshot(DateRange range)
    {
        var now = DateTime.UtcNow;
        var snapshotStart = new DateTime(now.Year - 2, 1, 1);
        var snapshotEnd = new DateTime(now.Year, 12, 31);

        return range.StartDate.Date >= snapshotStart &&
               range.EndDate.Date <= snapshotEnd;
    }

    private static DateRange GetSnapshotQueryRange()
    {
        var now = DateTime.UtcNow;
        return new DateRange(
            new DateTime(now.Year - 2, 1, 1),
            new DateTime(now.Year, 12, 31));
    }

    public async Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            var selectedAccount = context == null ? null : _userAccountContextService.ResolveSelectedAccount(context, accountName);
            if (selectedAccount == null) return new List<ExpenseTransactionDto>();

            var accountingData = CanUseAccountSnapshot(dateRange)
                ? await LoadAccountingDataFromSnapshotAsync(selectedAccount, dateRange, Stopwatch.StartNew())
                : await LoadAccountingDataAsync(selectedAccount, dateRange);
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

            var accountingData = CanUseAccountSnapshot(dateRange)
                ? await LoadAccountingDataFromSnapshotAsync(selectedAccount, dateRange, Stopwatch.StartNew())
                : await LoadAccountingDataAsync(selectedAccount, dateRange);
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

            var accountingData = CanUseAccountSnapshot(dateRange)
                ? await LoadAccountingDataFromSnapshotAsync(selectedAccount, dateRange, Stopwatch.StartNew())
                : await LoadAccountingDataAsync(selectedAccount, dateRange);
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