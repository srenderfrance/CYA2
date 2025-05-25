using System;


/* USED to Add accountd from the etapestry table
 public async Task CreateAccountsFromFunds()
    {
        try
        {
            if (funds == null || !funds.Any())
            {
                errorMessage = "No funds available to create accounts from.";
                return;
            }

            successMessage = null;
            errorMessage = null;
            createdAccounts.Clear();
            int successCount = 0;
            int failCount = 0;

            foreach (var fund in funds)
            {
                string name;
                string accountRef = fund;

                // Process the fund string
                if (fund.Contains(':'))
                {
                    int colonIndex = fund.IndexOf(':');
                    name = fund.Substring(0, colonIndex);
                }
                else
                {
                    name = fund;
                }

                try
                {
                    // Check if the account already exists
                    string checkSql = "SELECT COUNT(*) FROM Accounts WHERE AccountRef = @AccountRef";
                    var accountExists = await _data.LoadData<int, dynamic>(
                        checkSql,
                        new { AccountRef = accountRef },
                        _config.GetConnectionString("default"));

                    if (accountExists != null && accountExists.FirstOrDefault() > 0)
                    {
                        createdAccounts.Add((name, accountRef, false, "Account already exists"));
                        failCount++;
                        continue;
                    }

                    // Insert the account
                    string insertSql = @"
                        INSERT INTO Accounts (Name, AccountRef, CreatedAt)
                        VALUES (@Name, @AccountRef, @DateCreated)";

                    int result = await _data.SaveData(
                        insertSql,
                        new
                        {
                            Name = name,
                            AccountRef = accountRef,
                            DateCreated = DateTime.Now
                        },
                        _config.GetConnectionString("default")
                    );

                    if (result > 0)
                    {
                        createdAccounts.Add((name, accountRef, true, string.Empty));
                        successCount++;
                    }
                    else
                    {
                        createdAccounts.Add((name, accountRef, false, "Insert failed"));
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    createdAccounts.Add((name, accountRef, false, ex.Message));
                    failCount++;
                }
            }

            if (successCount > 0)
            {
                successMessage = $"Successfully created {successCount} account(s).";
                if (failCount > 0)
                {
                    successMessage += $" Failed to create {failCount} account(s).";
                }
            }
            else if (failCount > 0)
            {
                errorMessage = $"Failed to create {failCount} account(s). No accounts were created.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error creating accounts: {ex.Message}";
        }
    }
*/
