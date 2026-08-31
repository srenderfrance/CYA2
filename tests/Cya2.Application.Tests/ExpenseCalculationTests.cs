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
        Assert.Single(result.TransferTransactions);
        Assert.Single(result.OtherTransactions);
        Assert.Equal(10m, result.ExpenseTotal);
        Assert.Equal(20m, result.TransferTotal);
        Assert.Equal(30m, result.OtherTotal);
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
        public Task<List<AccountingRecord>> GetAccountingDataByClassAndDateAsync(string accountingClass, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
        public Task<List<AccountingRecord>> GetAccountingDataByClassOrAccountNumberAndDateAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
    }

    private sealed class EmptyDonationRepository : IDonationReadRepository
    {
        public Task<List<Cya2.Core.Entities.SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(new List<Cya2.Core.Entities.SubAccount>());
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetInternDonationsByDesignationAndDateRangeAsync(string internDesignationName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
    }
}