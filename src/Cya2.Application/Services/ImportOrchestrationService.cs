using System.Collections.Concurrent;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class ImportOrchestrationService : IImportOrchestrationService
{
    private readonly IReadOnlyDictionary<string, IImportProcessor> _processors;
    private readonly IImportProgressService _progressService;
    private readonly IImportWorkQueue _workQueue;
    private readonly ILogger<ImportOrchestrationService> _logger;
    private readonly ImportPreviewStore _previews;
    private readonly ImportUploadValidator _uploadValidator;
    private readonly IImportAuthorizationContext _authorizationContext;
    private readonly IAuditEventService _auditEvents;

    public ImportOrchestrationService(
        IEnumerable<IImportProcessor> processors,
        IImportProgressService progressService,
        IImportWorkQueue workQueue,
        ImportPreviewStore previews,
        ImportUploadValidator uploadValidator,
        IImportAuthorizationContext authorizationContext,
        IAuditEventService auditEvents,
        ILogger<ImportOrchestrationService> logger)
    {
        _processors = processors.ToDictionary(p => p.ImportType, StringComparer.OrdinalIgnoreCase);
        _progressService = progressService;
        _workQueue = workQueue;
        _previews = previews;
        _uploadValidator = uploadValidator;
        _authorizationContext = authorizationContext;
        _auditEvents = auditEvents;
        _logger = logger;
    }

    public async Task<FilePreviewResult> PreviewAsync(
        Stream file,
        string importType,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        GetProcessor(importType);

        var actor = await _authorizationContext.GetCurrentActorAsync();
        if (actor is null)
            throw new ImportAuthorizationException();

        var data = await _uploadValidator.ReadAndValidateAsync(file, fileName, cancellationToken);

        var previewId = Guid.NewGuid().ToString("N");
        if (!_previews.TrySet(previewId, new ImportPreview(data, fileName ?? string.Empty, contentType ?? string.Empty, DateTime.UtcNow, actor.UserId, importType)))
            throw new ImportUploadCapacityException("The application is at its limit for retained upload previews. Please try again after an existing preview is imported or expires.");

        _logger.LogInformation("Created {ImportType} import preview {PreviewId} ({Size} bytes)", importType, previewId, data.Length);

        return new FilePreviewResult
        {
            PreviewId = previewId,
            FileName = fileName ?? string.Empty,
            FileSizeBytes = data.Length,
            ContentType = contentType ?? string.Empty
        };
    }

    public async Task<ImportResult> ImportFromPreviewAsync(string previewId, string importType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(previewId))
            throw new ArgumentException("PreviewId is required", nameof(previewId));

        var actor = await _authorizationContext.GetCurrentActorAsync();
        if (actor is null || !_previews.TryRemoveOwned(previewId, actor.UserId, importType, out var preview) || preview is null)
        {
            var expired = new ImportResult();
            expired.Errors.Add("Preview session expired. Please upload the file again.");
            return expired;
        }

        var progressId = Guid.NewGuid().ToString("N");
        using var operation = AuditOperationScope.Begin(int.TryParse(actor.UserId, out var actorId) ? actorId : null, progressId);
        _progressService.Start(progressId, importType, actor.UserId);
        try
        {
            await RecordImportAuditAsync(actor.UserId, importType, AuditOutcomes.Started, cancellationToken: cancellationToken);
            await using var stream = new MemoryStream(preview.Data, writable: false);
            var importResult = await GetProcessor(importType).ProcessAsync(stream, progressId, cancellationToken);
            try
            {
                if (!importResult.PublicationCommitted)
                    await RecordImportAuditAsync(actor.UserId, importType, AuditOutcomes.Failed, importResult.InsertedRows, CancellationToken.None);
            }
            catch
            {
                importResult.Warnings.Add("The upload result could not be recorded in the audit log.");
                _logger.LogWarning("Import terminal audit delivery failed.");
            }
            return importResult;
        }
        catch (Exception ex)
        {
            var message = ex is ImportOutcomeUnknownException ? ex.Message
                : ex is OperationCanceledException ? "Import cancelled before publication completed."
                : "Import failed. Existing data was not replaced unless the commit outcome requires reconciliation.";
            _progressService.AddErrors(progressId, [message]);
            _progressService.SetStatus(progressId, message);
            _progressService.Complete(progressId);
            await RecordImportAuditAsync(actor.UserId, importType,
                ex is ImportOutcomeUnknownException ? AuditOutcomes.Unknown : ex is OperationCanceledException ? AuditOutcomes.Cancelled : AuditOutcomes.Failed,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    public async Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null)
    {
        var result = new ImportResult { ProgressId = Guid.NewGuid().ToString("N") };
        var actor = await _authorizationContext.GetCurrentActorAsync();
        if (actor is null)
        {
            result.Errors.Add("The import is no longer available.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(previewId))
        {
            result.Errors.Add("PreviewId is required");
            _progressService.SetStatus(result.ProgressId, "PreviewId is required");
            return result;
        }

        if (!_previews.TryRemoveOwned(previewId, actor.UserId, importType, out var preview) || preview is null)
        {
            result.Errors.Add("Preview session expired. Please upload the file again.");
            result.ProgressId = null;
            return result;
        }

        _progressService.Start(result.ProgressId!, importType, actor.UserId);
        using var operation = AuditOperationScope.Begin(int.TryParse(actor.UserId, out var auditActorId) ? auditActorId : null, result.ProgressId);
        var id = result.ProgressId;
        try
        {
            await RecordImportAuditAsync(actor.UserId, importType, AuditOutcomes.Started);
            _logger.LogInformation("Queueing background {ImportType} import. PreviewId={PreviewId}, ProgressId={ProgressId}", importType, previewId, id);
            await _workQueue.EnqueueAsync(new ImportWorkItem(
                importType,
                previewId,
                id,
                int.TryParse(actor.UserId, out var actorUserId) ? actorUserId : null,
                preview.Data));
        }
        catch (Exception ex)
        {
            await RecordImportAuditAsync(actor.UserId, importType, ex is OperationCanceledException ? AuditOutcomes.Cancelled : AuditOutcomes.Failed, cancellationToken: CancellationToken.None);
            _progressService.SetStatus(id!, "Import did not start. No publication was attempted.");
            _progressService.Complete(id!);
            throw;
        }

        return result;
    }

    private IImportProcessor GetProcessor(string importType)
        => _processors.TryGetValue(importType, out var processor)
            ? processor
            : throw new ArgumentException($"Invalid import type: {importType}", nameof(importType));

    private Task RecordImportAuditAsync(
        string actorUserId,
        string importType,
        string outcome,
        int? itemCount = null,
        CancellationToken cancellationToken = default)
    {
        var eventType = string.Equals(importType, "donations", StringComparison.OrdinalIgnoreCase)
            ? outcome switch
            {
                AuditOutcomes.Started => AuditEventTypes.DonationImportStarted,
                AuditOutcomes.Succeeded => AuditEventTypes.DonationImportCompleted,
                _ => AuditEventTypes.DonationImportFailed
            }
            : outcome switch
            {
                AuditOutcomes.Started => AuditEventTypes.AccountingImportStarted,
                AuditOutcomes.Succeeded => AuditEventTypes.AccountingImportCompleted,
                _ => AuditEventTypes.AccountingImportFailed
            };

        var actorId = int.TryParse(actorUserId, out var parsedActorId) ? parsedActorId : (int?)null;
        if (outcome == AuditOutcomes.Started)
            return _auditEvents.RecordRequiredAsync(actorId, eventType, outcome, itemCount: itemCount, cancellationToken: cancellationToken);
        return _auditEvents.RecordAsync(
            actorId,
            eventType,
            outcome,
            itemCount: itemCount,
            cancellationToken: cancellationToken);
    }
}
