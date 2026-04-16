using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class ExpenseReadRepository : IExpenseReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public ExpenseReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<AccountingRecord>> GetAccountingDataByClassAndDateAsync(string accountingClass, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
FROM AccountingData
WHERE AccountingClass = @AccountClass
  AND Date >= @StartDate
  AND Date <= @EndDate
ORDER BY Date DESC";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<AccountingRecord>(sql, new { AccountClass = accountingClass, StartDate = startDate, EndDate = endDate });
        return rows.ToList();
    }

    public async Task<List<AccountingRecord>> GetAccountingDataByClassOrAccountNumberAndDateAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
FROM AccountingData
WHERE (AccountingClass = @AccountClass OR AccountNumber = @AccountNumber)
  AND Account != 'Prepaids'
  AND Date >= @StartDate
  AND Date <= @EndDate
ORDER BY Date";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<AccountingRecord>(sql, new { AccountClass = accountingClass, AccountNumber = accountNumber, StartDate = startDate, EndDate = endDate });
        return rows.ToList();
    }
}
