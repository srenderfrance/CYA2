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

            const string accountsSql = "SELECT AccountId, Fund, AccountingClass, FundNumber, CreatedAt, Balance FROM Accounts ORDER BY Fund";
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
                    if (string.IsNullOrWhiteSpace(account.AccountingClass))
                    {
                        Console.WriteLine($"BalanceUpdater: empty class for '{account.Fund}', setting balance=0.");
                        await PersistBalanceAsync(account.AccountId, 0m, conn);
                        updated++;
                        continue;
                    }

                    // Aggregate signed amounts from AccountingData for the exact AccountingClass
                    const string sumExactSql = @"
                        SELECT COALESCE(SUM(Amount), 0) AS Net
                        FROM AccountingData
                        WHERE AccountingClass = @Class
                          AND (Type IS NULL OR LOWER(Type) NOT LIKE '%fundraising%')";

                    var exactRes = await _data.LoadData<NetResult, dynamic>(
                        sumExactSql,
                        new { Class = account.AccountingClass },
                        conn);

                    decimal net = exactRes?.FirstOrDefault()?.Net ?? 0m;

                    Console.WriteLine($"BalanceUpdater: '{account.Fund}' class='{account.AccountingClass}' Net={net}");

                    var affected = await PersistBalanceAsync(account.AccountId, net, conn);
                    if (affected <= 0)
                    {
                        Console.WriteLine($"BalanceUpdater: WARNING update affected {affected} rows for AccountId={account.AccountId}");
                    }

                    updated++;

                    if (updated % 10 == 0)
                    {
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"BalanceUpdater: ERROR updating '{account.Fund}' - {ex.Message}");
                }
            }

            return updated;
        }

        public async Task<List<Account>> GetAllAccountBalancesAsync()
        {
            var conn = _config.GetConnectionString("default");
            const string sql = "SELECT AccountId, Fund, AccountingClass, FundNumber, CreatedAt, Balance FROM Accounts ORDER BY Fund";
            var accounts = await _data.LoadData<Account, dynamic>(sql, new { }, conn);
            return accounts?.ToList() ?? new List<Account>();
        }

        private async Task<int> PersistBalanceAsync(int accountId, decimal balance, string conn)
        {
            const string updateSql = "UPDATE Accounts SET Balance = @Balance WHERE AccountId = @AccountId";
            return await _data.SaveData(updateSql, new { Balance = balance, AccountId = accountId }, conn);
        }

        private sealed class NetResult
        {
            public decimal Net { get; set; }
        }
    }
}