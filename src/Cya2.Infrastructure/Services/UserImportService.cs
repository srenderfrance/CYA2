using System.Globalization;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace Cya2.Infrastructure.Services;

public sealed class UserImportService(
    IAdminAuthorizationContext authorization,
    IUserRepository users,
    IUserBatchAdministrationStore store,
    IImportCacheInvalidator cacheInvalidator,
    IDatabaseGuard guard,
    IAuditEventService auditEvents,
    IAdminPreloadService preload,
    ILogger<UserImportService> logger) : IUserImportService
{
    private int? _previewActor;
    private ImportUserEntry[]? _preview;
    private int _previewSkippedCount;

    public async Task<UserImportPreviewDto> PreviewAsync(Stream workbook, string fileName, CancellationToken cancellationToken = default)
    {
        _preview = null;
        _previewSkippedCount = 0;
        _previewActor = await authorization.GetCurrentAdminUserIdAsync();
        if (_previewActor is null) return Invalid("Administrator authorization is required.");
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(Path.GetExtension(safeFileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return Invalid("Select an .xlsx workbook.");
        if (workbook.CanSeek && workbook.Length > 10 * 1024 * 1024) return Invalid("The workbook exceeds the 10 MB limit.");
        try
        {
            using var package = new ExcelPackage(workbook);
            var sheet = package.Workbook.Worksheets.FirstOrDefault();
            if (sheet?.Dimension is null) return Invalid("The workbook does not contain a worksheet with data.");
            if (sheet.Dimension.End.Row > 10001 || sheet.Dimension.End.Column > 256) return Invalid("The workbook exceeds the supported row or column limit.");
            var headerRow = sheet.Dimension.Start.Row;
            var userColumns = new List<int>();
            var levelColumns = new List<int>();
            for (var column = sheet.Dimension.Start.Column; column <= sheet.Dimension.End.Column; column++)
            {
                var header = sheet.Cells[headerRow, column].Text.Trim();
                if (header.Equals("User", StringComparison.OrdinalIgnoreCase)) userColumns.Add(column);
                if (header.Equals("Level", StringComparison.OrdinalIgnoreCase)) levelColumns.Add(column);
            }
            if (userColumns.Count != 1 || levelColumns.Count != 1) return Invalid("The workbook must contain exactly one User column and one Level column.");
            var rows = new List<UserImportRowDto>();
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            guard.ThrowIfUnavailable();
            for (var row = headerRow + 1; row <= sheet.Dimension.End.Row; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var email = sheet.Cells[row, userColumns[0]].Text.Trim();
                var level = sheet.Cells[row, levelColumns[0]].Text.Trim();
                if (email.Length == 0 && level.Length == 0) continue;
                var role = level.ToLowerInvariant() switch { "admin" => "Admin", "normal" => "User", "view all" => "Viewer", _ => null };
                var local = email.Split('@')[0].Split('.');
                if (!UserManagementService.IsOrganizationEmail(email) || local.Length != 2 || local.Any(string.IsNullOrWhiteSpace))
                {
                    errors.Add($"Row {row}: A firstname.lastname@scene8.net email is required.");
                    continue;
                }
                if (role is null) errors.Add($"Row {row}: Level must be admin, normal, or view all.");
                if (!seen.Add(email)) errors.Add($"Row {row}: Duplicate user email in workbook.");
                var isExisting = await users.ExistsAsync(email);
                var name = string.Join(" ", local.Select(part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
                rows.Add(new UserImportRowDto(row, email, name, role ?? string.Empty, isExisting));
            }
            if (rows.Count == 0 && errors.Count == 0) errors.Add("The workbook contains no user rows.");
            if (errors.Count == 0)
            {
                _preview = rows.Where(row => !row.IsExisting).Select(row => new ImportUserEntry(row.Name, row.Email, row.AuthLevel)).ToArray();
                _previewSkippedCount = rows.Count(row => row.IsExisting);
            }
            return new UserImportPreviewDto { Rows = rows, Errors = errors };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException)
        {
            return Invalid("The workbook could not be read.");
        }
    }

    public async Task<UserImportResultDto> ImportAsync(CancellationToken cancellationToken = default)
    {
        var actor = await authorization.GetCurrentAdminUserIdAsync();
        var preview = _preview;
        var skippedCount = _previewSkippedCount;
        _preview = null;
        _previewSkippedCount = 0;
        using var operation = AuditOperationScope.Begin(actor);
        if (actor is null || actor != _previewActor)
        {
            await RecordOutcomeAsync(actor, AuditOutcomes.Denied);
            return new(false, "Administrator authorization is required.");
        }
        if (preview is null || preview.Length == 0) return new(false, "Load a valid preview before importing.");
        cancellationToken.ThrowIfCancellationRequested();
        var attempted = false;
        var committed = false;
        try
        {
            attempted = true;
            var result = await store.CreateBatchAsync(actor.Value, preview);
            if (result.Status != UserAdministrationStatus.Success)
            {
                await RecordOutcomeAsync(actor, AuditOutcomes.Denied);
                return new(false, result.Status == UserAdministrationStatus.DuplicateEmail ? "A user already exists; no users were imported. Reload the preview." : "The import was not authorized; no users were imported.");
            }
            committed = true;
            cacheInvalidator.Invalidate(CacheDataDomains.Authorization);
            preload.Invalidate();
            return new(true, $"Successfully imported {preview.Length:N0} user(s); existing users skipped: {skippedCount:N0}. Assign account access separately.");
        }
        catch (Exception ex)
        {
            logger.LogError("User batch import failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            if (!committed) await RecordOutcomeAsync(actor, attempted && ex is not AuditPersistenceException ? AuditOutcomes.Unknown : AuditOutcomes.Failed);
            try { cacheInvalidator.Invalidate(CacheDataDomains.Authorization); preload.Invalidate(); }
            catch (Exception followUp) { logger.LogError("User batch cache invalidation failed. ExceptionType={ExceptionType}", followUp.GetType().Name); }
            return committed
                ? new(true, "Users were created, but refreshing failed. Refresh the user list.")
                : new(false, "The import could not be confirmed. Check the user list before retrying.");
        }
    }

    private async Task RecordOutcomeAsync(int? actor, string outcome)
    {
        try
        {
            var eventType = outcome == AuditOutcomes.Denied ? AuditEventTypes.AdminUserChangeDenied : outcome == AuditOutcomes.Unknown ? AuditEventTypes.AdminUserChangeUnconfirmed : AuditEventTypes.AdminUserChangeFailed;
            await auditEvents.RecordAsync(actor, eventType, outcome, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogCritical(new EventId(9100, "AuditWriteFailed"), "Administration audit delivery failed. OperationId={OperationId} ExceptionType={ExceptionType}", AuditOperationScope.Current?.OperationId, ex.GetType().Name);
        }
    }

    private static UserImportPreviewDto Invalid(string message) => new() { Errors = [message] };
}
