using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;

namespace UtilityClasses
{
    public class AccountBalanceUpdater
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;

        public AccountBalanceUpdater(IDataAccess data, IConfiguration config)
        {
            _data = data;
            _config = config;
        }

        public async Task<int> RecalculateAndPersistAllBalancesAsync()
        {
            var conn = _config.GetConnectionString("default");

            const string accountsSql = "SELECT AccountId, Name, AccountRef, CreatedAt, Balance FROM Accounts ORDER BY Name";
            var accounts = await _data.LoadData<Account, dynamic>(accountsSql, new { }, conn) ?? new List<Account>();
            if (accounts.Count == 0)
            {
                Console.WriteLine("BalanceUpdater: no accounts found.");
                return 0;
            }

            int updated = 0;

            foreach (var account in accounts)
            {
                try
                {
                    var designation = ExtractDesignation(account.AccountRef);
                    if (string.IsNullOrWhiteSpace(designation))
                    {
                        Console.WriteLine($"BalanceUpdater: empty designation for '{account.Name}' (ref='{account.AccountRef}'), setting balance=0.");
                        await PersistBalanceAsync(account.AccountId, 0m, conn);
                        updated++;
                        continue;
                    }

                    // Aggregate signed amounts in SQL (fast) and guard against NULL amounts.
                    // Also exclude obvious fundraising rows (type contains 'fundraising' case-insensitive).
                    const string sumLikeSql = @"
                        SELECT COALESCE(SUM(amount), 0) AS Net
                        FROM quickbooks
                        WHERE designation LIKE @Designation
                          AND (type IS NULL OR LOWER(type) NOT LIKE '%fundraising%')";

                    const string sumExactSql = @"
                        SELECT COALESCE(SUM(amount), 0) AS Net
                        FROM quickbooks
                        WHERE designation = @Designation
                          AND (type IS NULL OR LOWER(type) NOT LIKE '%fundraising%')";

                    var likeRes = await _data.LoadData<NetResult, dynamic>(
                        sumLikeSql,
                        new { Designation = $"%{designation}%" },
                        conn);

                    decimal net = likeRes?.FirstOrDefault()?.Net ?? 0m;

                    // Fallback: try exact AccountRef if LIKE matched nothing
                    if (net == 0m)
                    {
                        var exactRes = await _data.LoadData<NetResult, dynamic>(
                            sumExactSql,
                            new { Designation = account.AccountRef },
                            conn);

                        net = exactRes?.FirstOrDefault()?.Net ?? 0m;
                    }

                    Console.WriteLine($"BalanceUpdater: '{account.Name}' designation='{designation}' Net={net}");

                    var affected = await PersistBalanceAsync(account.AccountId, net, conn);
                    if (affected <= 0)
                    {
                        Console.WriteLine($"BalanceUpdater: WARNING update affected {affected} rows for AccountId={account.AccountId}");
                    }

                    updated++;

                    // Yield periodically to reduce pressure on the DB and monitoring thread.
                    if (updated % 10 == 0)
                    {
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"BalanceUpdater: ERROR updating '{account.Name}' - {ex.Message}");
                    // Continue with next account rather than failing the whole batch
                }
            }

            return updated;
        }

        public async Task<List<Account>> GetAllAccountBalancesAsync()
        {
            var conn = _config.GetConnectionString("default");
            const string sql = "SELECT AccountId, Name, AccountRef, CreatedAt, Balance FROM Accounts ORDER BY Name";
            var accounts = await _data.LoadData<Account, dynamic>(sql, new { }, conn);
            return accounts?.ToList() ?? new List<Account>();
        }

        private static string ExtractDesignation(string accountRef)
        {
            if (string.IsNullOrWhiteSpace(accountRef)) return string.Empty;
            var idx = accountRef.IndexOf(':');
            return idx >= 0 ? accountRef[(idx + 1)..].Trim() : accountRef.Trim();
        }

        private async Task<int> PersistBalanceAsync(int accountId, decimal balance, string conn)
        {
            const string updateSql = "UPDATE Accounts SET Balance = @Balance WHERE AccountId = @AccountId";
            return await _data.SaveData(updateSql, new { Balance = balance, AccountId = accountId }, conn);
        }

        // Maps the aggregated SQL result
        private sealed class NetResult
        {
            public decimal Net { get; set; }
        }
    }
}