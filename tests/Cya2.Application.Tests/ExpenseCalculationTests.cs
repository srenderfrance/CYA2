using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ExpenseCalculationTests
{
    [Fact]
    public void Classify_IsCaseInsensitiveAndGivesExpensesPrecedenceOverTransfers()
    {
        var classifier = new ExpenseClassificationService();
        var transactions = new List<AccountingRecord>
        {
            new() { Type = "expense", Account = "Transfer: Supplies", Amount = 10 },
            new() { Account = "transfer: operating", Amount = 20 },
            new() { Account = "Income", Amount = 30 }
        };

        var result = classifier.Categorize(transactions);

        Assert.Single(result.ExpenseTransactions);
        Assert.Equal(1, result.TransferTransactions.Count);
        Assert.Single(result.OtherTransactions);
        Assert.Equal(10m, result.ExpenseTotal);
        Assert.Equal(20m, result.TransferTotal);
        Assert.Equal(30m, result.OtherTotal);
    }

    [Fact]
    public void AccountingTransactionProcessor_AppliesMatchingBeforeCategorizationAndBalance()
    {
        var processor = new AccountingTransactionProcessor(new ExpenseClassificationService());
        var account = new UserAccountContextAccount
        {
            AccountingClass = "Fund Class",
            AccountNumber = "123",
            BalanceAdjustment = 5m
        };
        var candidates = new List<AccountingRecord>
        {
            new() { AccountingClass = "Fund Class", AccountNumber = "999", Type = "Expense", Amount = 10 },
            new() { AccountingClass = "Other Class", AccountNumber = "123", Account = "Transfer: Incoming", Amount = 20 },
            new() { AccountingClass = "Other Class", AccountNumber = "999", Amount = 100 },
            new() { AccountingClass = "Fund Class", AccountNumber = "999", Account = "Payroll Clearing Insurance", Amount = 30 }
        };

        var result = processor.Process(account, candidates);

        Assert.Equal(15m, result.TotalBalance);
        Assert.Equal(10m, result.ExpenseTotal);
        Assert.Equal(20m, result.TransferTotal);
        Assert.Equal(2, result.AllTransactions.Count);
        Assert.Single(result.ExpenseTransactions);
        Assert.Single(result.TransferTransactions);
    }

    [Fact]
    public void TransferTotal_UsesSignedAmounts_AndNeverIncludesExpenses()
    {
        var classifier = new ExpenseClassificationService();
        var transactions = new List<AccountingRecord>
        {
            new() { Account = "Transfer: incoming", Amount = 20 },
            new() { Account = "Transfer: outgoing", Amount = -7 },
            new() { Account = "2200000 Unrestricted:General", Amount = 3 },
            new() { Type = "Expense", Account = "Transfer: excluded", Amount = 100 }
        };

        var result = classifier.Categorize(transactions);

        Assert.Equal(16m, result.TransferTotal);
        Assert.Equal(100m, result.ExpenseTotal);
        Assert.Equal(3, result.TransferTransactions.Count);
        Assert.Single(result.ExpenseTransactions);
        Assert.Empty(result.TransferTransactions.Intersect(result.ExpenseTransactions));
    }

    [Fact]
    public void FundraisingTransactions_ReturnToTheirOriginalCategories()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var transactions = new List<AccountingRecord>
        {
            new() { Account = "Fundraising: event", Amount = 100 },
            new() { Type = "Expense", Account = "Fundraising: supplies", Amount = 25 },
            new() { Account = "Transfer: operating", Amount = 10 }
        };

        var result = service.CalculateBalanceFromData(transactions);

        Assert.Equal(85m, result.TotalBalance);
        Assert.Equal(25m, result.ExpenseTotal);
        Assert.Equal(10m, result.TransferTotal);
        Assert.Equal(100m, result.OtherTotal);
        Assert.Equal(3, result.AllTransactions.Count);
    }

    [Fact]
    public void PayrollClearingInsurance_IsExcludedFromBalanceCalculations()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var transactions = new List<AccountingRecord>
        {
            new() { Account = "Payroll Clearing Insurance", Amount = 100 },
            new() { Account = "Income", Amount = 25 },
            new() { Type = "Expense", Account = "Expenses: Supplies", Amount = 10 }
        };

        var result = service.CalculateBalanceFromData(transactions);

        Assert.Equal(15m, result.TotalBalance);
        Assert.Equal(10m, result.ExpenseTotal);
        Assert.Equal(25m, result.OtherTotal);
        Assert.DoesNotContain(result.AllTransactions, transaction =>
            string.Equals(transaction.Account, "Payroll Clearing Insurance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prepaids_IsExcludedFromBalanceCalculations()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var transactions = new List<AccountingRecord>
        {
            new() { Account = "Prepaids", Amount = 100 },
            new() { Account = "Income", Amount = 25 }
        };

        var result = service.CalculateBalanceFromData(transactions);

        Assert.Equal(25m, result.TotalBalance);
        Assert.Equal(0m, result.ExpenseTotal);
        Assert.Equal(0m, result.TransferTotal);
        Assert.Equal(25m, result.OtherTotal);
        Assert.DoesNotContain(result.AllTransactions, transaction =>
            string.Equals(transaction.Account, "Prepaids", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GeneralGivingGeneralAccount_IsIncludedInBalanceButNotExpenseOrTransfer()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var transactions = new List<AccountingRecord>
        {
            new()
            {
                AccountingClass = "General Administration:Fundraising:General Giving",
                AccountNumber = "2200000",
                Account = "Unrestricted:General",
                Amount = 100
            },
            new()
            {
                AccountingClass = "General Administration:Fundraising:General Giving",
                Account = "2200000 Unrestricted:General",
                Amount = 25
            }
        };

        var result = service.CalculateBalanceFromData(transactions);

        Assert.Equal(125m, result.TotalBalance);
        Assert.Equal(0m, result.ExpenseTotal);
        Assert.Equal(0m, result.TransferTotal);
        Assert.Equal(125m, result.OtherTotal);
        Assert.Equal(2, result.AllTransactions.Count);
        Assert.Equal(2, result.OtherTransactions.Count);
    }

    [Fact]
    public void AccountNumber2200000_MatchesAccountingClassOnly()
    {
        var matchingClass = "General Administration:Fundraising:General Giving";
        var rows = new List<AccountingRecord>
        {
            new() { AccountingClass = matchingClass, AccountNumber = string.Empty, Account = "Other", Amount = 10 },
            new() { AccountingClass = "Other Class", AccountNumber = "2200000", Account = "Other", Amount = 20 },
            new() { AccountingClass = matchingClass, AccountNumber = "9999999", Account = "Other", Amount = 30 }
        };

        var matching = rows.Where(row => AccountingDataMatcher.MatchesAccount(row, matchingClass, "2200000")).ToList();
        var ordinaryAccountMatching = rows.Where(row => AccountingDataMatcher.MatchesAccount(row, "Different Class", "9999999")).ToList();

        Assert.Equal(2, matching.Count);
        Assert.Contains(matching, row => row.Amount == 10d);
        Assert.Contains(matching, row => row.Amount == 30d);
        Assert.Single(ordinaryAccountMatching);
    }

    [Fact]
    public void AccountNumberFadh_MatchesAccountingClassOnly()
    {
        var matchingClass = "FADH Class";
        var rows = new List<AccountingRecord>
        {
            new() { AccountingClass = matchingClass, AccountNumber = string.Empty, Amount = 10 },
            new() { AccountingClass = "Other Class", AccountNumber = "FADH", Amount = 20 },
            new() { AccountingClass = matchingClass, AccountNumber = "9999999", Amount = 30 }
        };

        var matching = rows.Where(row => AccountingDataMatcher.MatchesAccount(row, matchingClass, "FADH")).ToList();

        Assert.Equal(2, matching.Count);
        Assert.Contains(matching, row => row.Amount == 10d);
        Assert.Contains(matching, row => row.Amount == 30d);
    }

    [Fact]
    public async Task CalculateBalancesAsync_UsesTheSharedBalanceFormulaForEveryAccount()
    {
        var repository = new BatchExpenseRepository();
        var service = new AccountCalculationService(
            repository,
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var accounts = new List<UserAccountContextAccount>
        {
            new() { AccountId = 1, AccountingClass = "Class1", AccountNumber = "Number1", BalanceAdjustment = 5m },
            new() { AccountId = 2, AccountingClass = "Class2", AccountNumber = "Number2" }
        };

        var results = await service.CalculateBalancesAsync(accounts, DateTime.MinValue, DateTime.MaxValue);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(15m, results[1].TotalBalance);
        Assert.Equal(20m, results[2].TotalBalance);
        Assert.DoesNotContain(results[1].AllTransactions, transaction =>
            string.Equals(transaction.Account, "Payroll Clearing Insurance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Categorize_UsesStoredExpenseAmountForTotal()
    {
        var classifier = new ExpenseClassificationService();

        var result = classifier.Categorize(new List<AccountingRecord>
        {
            new() { Type = "Expense", Amount = -25 }
        });

        Assert.Equal(-25m, result.ExpenseTotal);
    }

    private sealed class BatchExpenseRepository : IExpenseReadRepository
    {
        public int CallCount { get; private set; }

        public Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());

        public Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts, DateTime startDate, DateTime endDate)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyDictionary<int, List<AccountingRecord>>>(new Dictionary<int, List<AccountingRecord>>
            {
                [1] =
                [
                    new() { AccountingClass = "Class1", AccountNumber = "Number1", Account = "Payroll Clearing Insurance", Amount = 100 },
                    new() { AccountingClass = "Class1", AccountNumber = "Number1", Account = "Income", Amount = 10 }
                ],
                [2] =
                [
                    new() { AccountingClass = "Class2", AccountNumber = "Number2", Account = "Transfer: operating", Amount = 20 }
                ]
            });
        }
    }

    [Fact]
    public void DonationTotals_IncludeMergedButRetainSeparateSubaccounts()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var account = new UserAccountContextAccount { AccountId = 1, Fund = "PRIMARY" };
        var subAccounts = new List<SubAccount>
        {
            new(1, "MERGED", "Merged"),
            new(1, "SEPARATE", "Separate")
        };
        var donations = new List<DonationRecord>
        {
            new() { Fund = "PRIMARY", Amount = 100 },
            new() { Fund = "MERGED", Amount = 25 },
            new() { Fund = "SEPARATE", Amount = 50 }
        };

        var result = service.CalculateDonationTotalsFromData(account, donations, subAccounts);

        Assert.Equal(125m, result.TotalDonations);
        Assert.Equal(25m, result.MergedSubfundDonations);
        Assert.Equal(50m, result.SeparateSubfundTotals["SEPARATE"]);
    }

    [Fact]
    public void DonationTotals_TrimSubaccountKindWhenIncludingMergedFunds()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var account = new UserAccountContextAccount { AccountId = 1, Fund = "PRIMARY" };
        var subAccounts = new List<SubAccount>
        {
            new(1, "MERGED", " Merged ")
        };
        var donations = new List<DonationRecord>
        {
            new() { Fund = "PRIMARY", Amount = 100 },
            new() { Fund = "MERGED", Amount = 25 }
        };

        var result = service.CalculateDonationTotalsFromData(account, donations, subAccounts);

        Assert.Equal(125m, result.TotalDonations);
        Assert.Equal(25m, result.MergedSubfundDonations);
    }

    [Fact]
    public void HomeDonationTotals_MatchCombinedDonationScopeWithoutSeparateSubaccounts()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var account = new UserAccountContextAccount { AccountId = 1, Fund = "PRIMARY" };
        var subAccounts = new List<SubAccount>
        {
            new(1, "MERGED", "Merged"),
            new(1, "SEPARATE", "Separate")
        };
        var donations = new List<DonationRecord>
        {
            new() { Fund = "PRIMARY", Amount = 100 },
            new() { Fund = "MERGED", Amount = 25 },
            new() { Fund = "SEPARATE", Amount = 50 }
        };

        var result = service.CalculateDonationTotalsFromData(account, donations, subAccounts);

        Assert.Equal(125m, result.TotalDonations);
        Assert.Equal(50m, result.SeparateSubfundTotals["SEPARATE"]);
    }

    [Fact]
    public void DonationTotals_InternAccountUsesAllMatchedRowsAsTotal()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var account = new UserAccountContextAccount { AccountId = 2, Fund = "Intern: Jane Doe" };
        var donations = new List<DonationRecord>
        {
            new() { Fund = "unrelated", Intern = "Jane Doe", Amount = 80 },
            new() { Fund = "unrelated", Intern = "Doe, Jane", Amount = 20 }
        };

        var result = service.CalculateDonationTotalsFromData(account, donations, []);

        Assert.Equal(100m, result.TotalDonations);
        Assert.Equal(0m, result.MergedSubfundDonations);
        Assert.Empty(result.SeparateSubfundTotals);
    }

    [Fact]
    public void CalculateOverheadAmount_UsesCoreAccountRule()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var account = new UserAccountContextAccount { Overhead = 12.5m };

        var result = service.CalculateOverheadAmount(account, 100m);

        Assert.Equal(new Account { Overhead = 12.5m }.CalculateOverheadAmount(100m), result);
        Assert.Equal(12.5m, result);
    }

    [Fact]
    public void CalculateBalanceFromData_UsesCoreClassificationForTotalsAndBalance()
    {
        var service = new AccountCalculationService(
            new EmptyExpenseRepository(),
            new EmptyDonationRepository(),
            new ExpenseClassificationService());
        var transactions = new List<AccountingRecord>
        {
            new() { Type = "EXPENSE", Amount = 10 },
            new() { Account = "TRANSFER: operating", Amount = 20 },
            new() { Account = "Income", Amount = 30 }
        };

        var result = service.CalculateBalanceFromData(transactions, 5m);

        Assert.Equal(45m, result.TotalBalance);
        Assert.Equal(10m, result.ExpenseTotal);
        Assert.Equal(20m, result.TransferTotal);
        Assert.Equal(30m, result.OtherTotal);
    }

    private sealed class EmptyExpenseRepository : IExpenseReadRepository
    {
        public Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
        public Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts, DateTime startDate, DateTime endDate) => Task.FromResult<IReadOnlyDictionary<int, List<AccountingRecord>>>(new Dictionary<int, List<AccountingRecord>>());
    }

    private sealed class EmptyDonationRepository : IDonationReadRepository
    {
            public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<Cya2.Core.Entities.SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(new List<Cya2.Core.Entities.SubAccount>());
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetInternDonationsByDesignationAndDateRangeAsync(string internDesignationName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
    }
}