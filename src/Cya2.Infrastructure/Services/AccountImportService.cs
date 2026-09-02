using System.Globalization;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using OfficeOpenXml;

namespace Cya2.Infrastructure.Services;

public sealed class AccountImportService : IAccountImportService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IAccountRepository _accountRepository;
    private readonly IImportCacheInvalidator _cacheInvalidator;
    private readonly IAdminPreloadService _adminPreloadService;
    private readonly IDatabaseGuard _dbGuard;

    public AccountImportService(
        IHostEnvironment environment,
        IConfiguration configuration,
        IAccountRepository accountRepository,
        IImportCacheInvalidator cacheInvalidator,
        IAdminPreloadService adminPreloadService,
        IDatabaseGuard dbGuard)
    {
        _environment = environment;
        _configuration = configuration;
        _accountRepository = accountRepository;
        _cacheInvalidator = cacheInvalidator;
        _adminPreloadService = adminPreloadService;
        _dbGuard = dbGuard;
    }

    private string ImportDirectory => Path.Combine(_environment.ContentRootPath, "AccountImports");

    public async Task<AccountImportPreviewDto> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.Exists(ImportDirectory)
            ? Directory.GetFiles(ImportDirectory, "*.xlsx", SearchOption.TopDirectoryOnly)
            : [];

        if (files.Length == 0)
            return InvalidPreview("No .xlsx file was found in the AccountImports folder.");

        if (files.Length > 1)
            return InvalidPreview("More than one .xlsx file was found. Leave only the account import workbook in the folder.");

        var filePath = files[0];
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
                return InvalidPreview("The workbook does not contain a worksheet with data.", Path.GetFileName(filePath));

            var headers = FindHeaders(worksheet);
            var errors = new List<string>();
            foreach (var required in RequiredHeaders)
            {
                if (!headers.ContainsKey(required))
                    errors.Add($"Missing required column: {required}");
            }

            if (errors.Count > 0)
                return new AccountImportPreviewDto { FileName = Path.GetFileName(filePath), Errors = errors };

            var rows = new List<AccountImportRowDto>();
            for (var rowNumber = worksheet.Dimension.Start.Row + 1; rowNumber <= worksheet.Dimension.End.Row; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var accountNumber = CellText(worksheet, rowNumber, headers["AccountNumber"]);
                var fund = CellText(worksheet, rowNumber, headers["Fund"]);
                var accountingClass = CellText(worksheet, rowNumber, headers["AccountingClass"]);
                var overheadText = CellText(worksheet, rowNumber, headers["Overhead"]);

                if (string.IsNullOrWhiteSpace(accountNumber) &&
                    string.IsNullOrWhiteSpace(fund) &&
                    string.IsNullOrWhiteSpace(accountingClass) &&
                    string.IsNullOrWhiteSpace(overheadText))
                    continue;

                if (string.IsNullOrWhiteSpace(accountNumber)) errors.Add($"Row {rowNumber}: Account Number is required.");
                if (string.IsNullOrWhiteSpace(fund)) errors.Add($"Row {rowNumber}: Fund is required.");
                if (string.IsNullOrWhiteSpace(accountingClass)) errors.Add($"Row {rowNumber}: Class is required.");

                if (!TryParseOverhead(worksheet.Cells[rowNumber, headers["Overhead"]], out var overhead))
                    errors.Add($"Row {rowNumber}: Overhead '{overheadText}' is not a valid number from 0 through 100.");
                else if (overhead < 0 || overhead > 100)
                    errors.Add($"Row {rowNumber}: Overhead must be from 0 through 100.");

                rows.Add(new AccountImportRowDto
                {
                    RowNumber = rowNumber,
                    AccountNumber = accountNumber,
                    Fund = fund,
                    AccountingClass = accountingClass,
                    Overhead = overhead
                });
            }

            AddDuplicateErrors(rows, errors, row => row.Fund, "Fund");
            AddDuplicateErrors(rows, errors, row => row.AccountNumber, "Account Number");

            _dbGuard.ThrowIfUnavailable();
            var existingAccounts = await _accountRepository.GetAllAsync();
            foreach (var row in rows)
            {
                if (existingAccounts.Any(account => string.Equals(account.Fund.Trim(), row.Fund, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Row {row.RowNumber}: Fund '{row.Fund}' already exists in Accounts.");
                if (existingAccounts.Any(account => string.Equals(account.AccountNumber.Trim(), row.AccountNumber, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Row {row.RowNumber}: Account Number '{row.AccountNumber}' already exists in Accounts.");
            }

            return new AccountImportPreviewDto
            {
                FileName = Path.GetFileName(filePath),
                Rows = rows,
                Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            return InvalidPreview($"Unable to read the workbook: {ex.Message}", Path.GetFileName(filePath));
        }
    }

    public Task<AccountImportResultDto> ImportAsync(IReadOnlyList<AccountImportRowDto> rows, CancellationToken cancellationToken = default)
    {
        return ImportInternalAsync(rows, cancellationToken);
    }

    private async Task<AccountImportResultDto> ImportInternalAsync(
        IReadOnlyList<AccountImportRowDto> rows,
        CancellationToken cancellationToken)
    {
        if (rows == null || rows.Count == 0)
            return new AccountImportResultDto { Message = "There are no account rows to import." };

        var errors = new List<string>();
        AddDuplicateErrors(rows, errors, row => row.Fund, "Fund");
        AddDuplicateErrors(rows, errors, row => row.AccountNumber, "Account Number");
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Fund)) errors.Add($"Row {row.RowNumber}: Fund is required.");
            if (string.IsNullOrWhiteSpace(row.AccountNumber)) errors.Add($"Row {row.RowNumber}: Account Number is required.");
            if (string.IsNullOrWhiteSpace(row.AccountingClass)) errors.Add($"Row {row.RowNumber}: Class is required.");
            if (row.Overhead < 0 || row.Overhead > 100) errors.Add($"Row {row.RowNumber}: Overhead must be from 0 through 100.");
        }

        if (errors.Count > 0)
            return new AccountImportResultDto { Message = string.Join(" ", errors.Distinct(StringComparer.OrdinalIgnoreCase)) };

        _dbGuard.ThrowIfUnavailable();
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = @"
INSERT INTO Accounts (Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment)
VALUES (@Fund, @AccountingClass, @AccountNumber, UTC_TIMESTAMP(), @Overhead, '', 0)";

            foreach (var row in rows)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Fund = row.Fund.Trim(),
                            AccountingClass = row.AccountingClass.Trim(),
                            AccountNumber = row.AccountNumber.Trim(),
                            row.Overhead
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            _cacheInvalidator.InvalidateAll();
            _adminPreloadService.Invalidate();
            return new AccountImportResultDto
            {
                IsSuccess = true,
                ImportedCount = rows.Count,
                Message = $"Successfully imported {rows.Count:N0} account(s)."
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static readonly string[] RequiredHeaders = ["AccountNumber", "Fund", "AccountingClass", "Overhead"];

    private static Dictionary<string, int> FindHeaders(ExcelWorksheet worksheet)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = worksheet.Dimension!.Start.Column; column <= worksheet.Dimension.End.Column; column++)
        {
            var header = NormalizeHeader(worksheet.Cells[worksheet.Dimension.Start.Row, column].Text);
            var key = header switch
            {
                "accountnumber" or "accountsnumber" or "account#" => "AccountNumber",
                "fund" => "Fund",
                "class" or "accountingclass" => "AccountingClass",
                "overhead" => "Overhead",
                _ => null
            };
            if (key != null && !headers.ContainsKey(key)) headers[key] = column;
        }
        return headers;
    }

    private static string NormalizeHeader(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string CellText(ExcelWorksheet worksheet, int row, int column) =>
        worksheet.Cells[row, column].Text?.Trim() ?? string.Empty;

    private static bool TryParseOverhead(ExcelRange cell, out decimal overhead)
    {
        var text = cell.Text?.Trim() ?? string.Empty;
        var isPercent = text.EndsWith('%') || (cell.Style.Numberformat?.Format?.Contains('%', StringComparison.Ordinal) ?? false);
        var numericText = text.TrimEnd('%').Trim();
        if (!decimal.TryParse(numericText, NumberStyles.Any, CultureInfo.InvariantCulture, out overhead) &&
            !decimal.TryParse(numericText, NumberStyles.Any, CultureInfo.CurrentCulture, out overhead))
        {
            overhead = 0;
            return false;
        }

        if (isPercent && !text.EndsWith('%') && cell.Value is double or decimal)
            overhead *= 100;

        return true;
    }

    private static void AddDuplicateErrors(
        IEnumerable<AccountImportRowDto> rows,
        ICollection<string> errors,
        Func<AccountImportRowDto, string> selector,
        string label)
    {
        foreach (var group in rows.Where(row => !string.IsNullOrWhiteSpace(selector(row)))
                                  .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
                                  .Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate {label} '{group.Key}' appears on rows {string.Join(", ", group.Select(row => row.RowNumber))}.");
        }
    }

    private static AccountImportPreviewDto InvalidPreview(string error, string fileName = "") =>
        new() { FileName = fileName, Errors = [error] };
}
