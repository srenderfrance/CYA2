using System.Globalization;
using Cya2.Application.DTOs;
using Cya2.Application.Diagnostics;
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
    private readonly IAdminAuthorizationContext _authorization;
    private int? _previewActor;
    private IReadOnlyList<AccountImportRowDto>? _previewRows;

    public AccountImportService(
        IHostEnvironment environment,
        IConfiguration configuration,
        IAccountRepository accountRepository,
        IImportCacheInvalidator cacheInvalidator,
        IAdminPreloadService adminPreloadService,
        IDatabaseGuard dbGuard,
        IAdminAuthorizationContext authorization)
    {
        _environment = environment;
        _configuration = configuration;
        _accountRepository = accountRepository;
        _cacheInvalidator = cacheInvalidator;
        _adminPreloadService = adminPreloadService;
        _dbGuard = dbGuard;
        _authorization = authorization;
    }

    private string ImportDirectory => Path.Combine(_environment.ContentRootPath, "AccountImports");

    public async Task<AccountImportPreviewDto> PreviewAsync(CancellationToken cancellationToken = default)
    {
        _previewRows = null;
        _previewActor = await _authorization.GetCurrentAdminUserIdAsync();
        if (_previewActor is null) return InvalidPreview("Administrator authorization is required.");
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
            if (new FileInfo(filePath).Length > 10 * 1024 * 1024)
                return InvalidPreview("The workbook exceeds the 10 MB limit.");
            await using var stream = File.OpenRead(filePath);
            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
                return InvalidPreview("The workbook does not contain a worksheet with data.", Path.GetFileName(filePath));
            if (worksheet.Dimension.End.Row > 10001 || worksheet.Dimension.End.Column > 256)
                return InvalidPreview("The workbook exceeds the supported row or column limit.");

            var headers = FindHeaders(worksheet);
            var errors = new List<string>();
            foreach (var required in RequiredHeaders)
            {
                if (!headers.TryGetValue(required, out var column) || column < 0)
                    errors.Add($"Missing required column: {required}");
            }
            if (headers.TryGetValue("AccountingClass", out var classColumn) && classColumn < 0)
                errors.Add("The Class column appears more than once.");

            if (errors.Count > 0)
                return new AccountImportPreviewDto { FileName = Path.GetFileName(filePath), Errors = errors };

            var rows = new List<AccountImportRowDto>();
            for (var rowNumber = worksheet.Dimension.Start.Row + 1; rowNumber <= worksheet.Dimension.End.Row; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var accountNumber = CellText(worksheet, rowNumber, headers["AccountNumber"]);
                var fund = CellText(worksheet, rowNumber, headers["Fund"]);
                var accountingClass = headers.TryGetValue("AccountingClass", out var classIndex)
                    ? CellText(worksheet, rowNumber, classIndex) : string.Empty;
                var overheadText = CellText(worksheet, rowNumber, headers["Overhead"]);

                if (string.IsNullOrWhiteSpace(accountNumber) &&
                    string.IsNullOrWhiteSpace(fund) &&
                    string.IsNullOrWhiteSpace(accountingClass) &&
                    string.IsNullOrWhiteSpace(overheadText))
                    continue;

                if (string.IsNullOrWhiteSpace(accountNumber)) errors.Add($"Row {rowNumber}: Account Number is required.");
                if (string.IsNullOrWhiteSpace(fund)) errors.Add($"Row {rowNumber}: Fund is required.");
                if (accountNumber.Length > 255 || fund.Length > 255 || accountingClass.Length > 255)
                    errors.Add($"Row {rowNumber}: Values must not exceed 255 characters.");

                if (!TryParseOverhead(worksheet.Cells[rowNumber, headers["Overhead"]], out var overhead))
                    errors.Add(ImportDiagnosticMessages.InvalidCell(rowNumber, "Overhead"));
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
            AddDuplicateErrors(rows, errors, row => row.AccountingClass, "Class");
            if (rows.Count == 0) errors.Add("The workbook contains no account rows.");

            _dbGuard.ThrowIfUnavailable();
            var existingAccounts = await _accountRepository.GetAllAsync();
            foreach (var row in rows)
            {
                if (existingAccounts.Any(account => string.Equals(account.Fund.Trim(), row.Fund, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(ImportDiagnosticMessages.ExistingValue(row.RowNumber, "Fund"));
                if (existingAccounts.Any(account => string.Equals(account.AccountNumber.Trim(), row.AccountNumber, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(ImportDiagnosticMessages.ExistingValue(row.RowNumber, "Account Number"));
                if (!string.IsNullOrWhiteSpace(row.AccountingClass) && existingAccounts.Any(account => string.Equals(account.AccountingClass?.Trim(), row.AccountingClass, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(ImportDiagnosticMessages.ExistingValue(row.RowNumber, "Class"));
            }

            if (errors.Count == 0 && rows.Count > 0)
                _previewRows = rows.Select(row => new AccountImportRowDto { RowNumber = row.RowNumber, Fund = row.Fund, AccountNumber = row.AccountNumber, AccountingClass = row.AccountingClass, Overhead = row.Overhead }).ToArray();

            return new AccountImportPreviewDto
            {
                FileName = Path.GetFileName(filePath),
                Rows = rows,
                Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            return InvalidPreview(ImportDiagnosticMessages.WorkbookReadFailure(), Path.GetFileName(filePath));
        }
    }

    public Task<AccountImportResultDto> ImportAsync(CancellationToken cancellationToken = default)
    {
        return ImportInternalAsync(cancellationToken);
    }

    private async Task<AccountImportResultDto> ImportInternalAsync(
        CancellationToken cancellationToken)
    {
        var actor = await _authorization.GetCurrentAdminUserIdAsync();
        var rows = _previewRows;
        _previewRows = null;
        if (actor is null || actor != _previewActor)
            return new AccountImportResultDto { Message = "Administrator authorization is required." };
        if (rows == null || rows.Count == 0)
            return new AccountImportResultDto { Message = "Load a valid preview before importing." };

        var errors = new List<string>();
        AddDuplicateErrors(rows, errors, row => row.Fund, "Fund");
        AddDuplicateErrors(rows, errors, row => row.AccountNumber, "Account Number");
        AddDuplicateErrors(rows, errors, row => row.AccountingClass, "Class");
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Fund)) errors.Add($"Row {row.RowNumber}: Fund is required.");
            if (string.IsNullOrWhiteSpace(row.AccountNumber)) errors.Add($"Row {row.RowNumber}: Account Number is required.");
            if (row.Overhead < 0 || row.Overhead > 100) errors.Add($"Row {row.RowNumber}: Overhead must be from 0 through 100.");
            if (row.Fund.Length > 255 || row.AccountNumber.Length > 255 || row.AccountingClass.Length > 255) errors.Add($"Row {row.RowNumber}: Values must not exceed 255 characters.");
        }

        if (errors.Count > 0)
            return new AccountImportResultDto { Message = string.Join(" ", errors.Distinct(StringComparer.OrdinalIgnoreCase)) };

        _dbGuard.ThrowIfUnavailable();
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committing = false;
        try
        {
            var actorRole = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT AuthLevel FROM Users WHERE Id=@actor FOR UPDATE", new { actor }, transaction, cancellationToken: cancellationToken));
            if (!string.Equals(actorRole?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AccountImportResultDto { Message = "Administrator authorization is required." };
            }
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
                            AccountingClass = string.IsNullOrWhiteSpace(row.AccountingClass) ? null : row.AccountingClass.Trim(),
                            AccountNumber = row.AccountNumber.Trim(),
                            row.Overhead
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken));
            }

            committing = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException ex) when (!committing && ex.Number == 1062)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new AccountImportResultDto { Message = "An account value already exists; no accounts were imported. Reload the preview." };
        }
        catch when (committing)
        {
            _cacheInvalidator.Invalidate(CacheDataDomains.AccountMetadata);
            _adminPreloadService.Invalidate();
            return new AccountImportResultDto { Message = "The import could not be confirmed. Check the account list before retrying." };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        try
        {
            _cacheInvalidator.Invalidate(CacheDataDomains.AccountMetadata);
            _adminPreloadService.Invalidate();
        }
        catch
        {
            return new AccountImportResultDto { IsSuccess = true, ImportedCount = rows.Count, Message = "Accounts were created, but refreshing failed. Refresh the account list." };
        }
        return new AccountImportResultDto { IsSuccess = true, ImportedCount = rows.Count, Message = $"Successfully imported {rows.Count:N0} account(s)." };
    }

    private static readonly string[] RequiredHeaders = ["AccountNumber", "Fund", "Overhead"];

    private static Dictionary<string, int> FindHeaders(ExcelWorksheet worksheet)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = worksheet.Dimension!.Start.Column; column <= worksheet.Dimension.End.Column; column++)
        {
            var rawHeader = worksheet.Cells[worksheet.Dimension.Start.Row, column].Text.Trim();
            var header = NormalizeHeader(rawHeader);
            if (rawHeader.Contains('#') && (header == "fund" || header == "account")) header = "accountnumber";
            var key = header switch
            {
                "accountnumber" or "accountsnumber" or "account#" => "AccountNumber",
                "fund" => "Fund",
                "class" or "accountingclass" => "AccountingClass",
                "overhead" => "Overhead",
                _ => null
            };
            if (key != null && !headers.TryAdd(key, column)) headers[key] = -1;
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
            errors.Add($"Duplicate {label} appears on rows {string.Join(", ", group.Select(row => row.RowNumber))}.");
        }
    }

    private static AccountImportPreviewDto InvalidPreview(string error, string fileName = "") =>
        new() { FileName = fileName, Errors = [error] };
}
