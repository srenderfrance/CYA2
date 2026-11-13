using System.Threading.Channels;
using Cya2.Application.Interfaces;
using Cya2.Application.Diagnostics;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Cya2.Core.Entities;

namespace cya2.Services.Imports;

public sealed class ImportWorkQueue : BackgroundService, IImportWorkQueue
{
    private readonly Channel<ImportWorkItem> _queue = Channel.CreateBounded<ImportWorkItem>(
        new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportWorkQueue> _logger;

    public ImportWorkQueue(IServiceScopeFactory scopeFactory, ILogger<ImportWorkQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _logger.LogInformation("Import work queue instance created.");
    }

    public async ValueTask EnqueueAsync(ImportWorkItem workItem, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Import work item enqueueing. ImportType={ImportType}, PreviewId={PreviewId}, ProgressId={ProgressId}, Bytes={Bytes}",
            workItem.ImportType, workItem.PreviewId, workItem.ProgressId, workItem.Data.Length);
        await _queue.Writer.WriteAsync(workItem, cancellationToken);
        _logger.LogInformation(
            "Import work item enqueued. ImportType={ImportType}, PreviewId={PreviewId}, ProgressId={ProgressId}",
            workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Import work queue worker started.");
            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                using var operation = AuditOperationScope.Begin(workItem.ActorUserId, workItem.ProgressId);
                _logger.LogInformation(
                    "Import work item dequeued. ImportType={ImportType}, PreviewId={PreviewId}, ProgressId={ProgressId}",
                    workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IEnumerable<IImportProcessor>>()
                        .Single(item => string.Equals(item.ImportType, workItem.ImportType, StringComparison.OrdinalIgnoreCase));
                    var auditEvents = scope.ServiceProvider.GetRequiredService<IAuditEventService>();
                    await using var stream = new MemoryStream(workItem.Data, writable: false);
                    _logger.LogInformation("Background {ImportType} processor started. PreviewId={PreviewId}, ProgressId={ProgressId}", workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                    var importResult = await processor.ProcessAsync(stream, workItem.ProgressId, stoppingToken);
                    try
                    {
                        if (!importResult.PublicationCommitted) await auditEvents.RecordAsync(
                        workItem.ActorUserId,
                        GetAuditEventType(workItem.ImportType, AuditOutcomes.Failed),
                        AuditOutcomes.Failed,
                        itemCount: importResult.InsertedRows,
                        cancellationToken: CancellationToken.None);
                    }
                    catch
                    {
                        _logger.LogWarning("Background import terminal audit delivery failed. ProgressId={ProgressId}", workItem.ProgressId);
                    }
                    _logger.LogInformation("Background {ImportType} processor returned. PreviewId={PreviewId}, ProgressId={ProgressId}", workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await ReportFailureAsync(workItem, new OperationCanceledException(), CancellationToken.None);
                    break;
                }
                catch (Exception ex)
                {
                    await ReportFailureAsync(workItem, ex, CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Import work queue worker stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Import work queue worker stopped unexpectedly. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw new InvalidOperationException("The import work queue worker stopped unexpectedly.");
        }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var pending))
                await ReportFailureAsync(pending, new OperationCanceledException(), CancellationToken.None);
        }
    }

    private async Task ReportFailureAsync(ImportWorkItem workItem, Exception exception, CancellationToken cancellationToken)
    {
        using var operation = AuditOperationScope.Begin(workItem.ActorUserId, workItem.ProgressId);
        var errorMessage = exception is ImportOutcomeUnknownException ? exception.Message
            : exception is OperationCanceledException ? "Import cancelled before publication completed."
            : ImportDiagnosticMessages.ImportFailed();
        if (exception is MySqlException mySqlException)
        {
            _logger.LogError(
                "Background {ImportType} import failed for preview {PreviewId}. ExceptionType={ExceptionType}, MySqlErrorCode={MySqlErrorCode}, SqlState={SqlState}",
                workItem.ImportType,
                workItem.PreviewId,
                ImportDiagnosticMessages.ExceptionCategory(exception),
                mySqlException.Number,
                mySqlException.SqlState);
        }
        else
        {
            _logger.LogError("Background {ImportType} import failed for preview {PreviewId}. ExceptionType={ExceptionType}",
                workItem.ImportType, workItem.PreviewId, ImportDiagnosticMessages.ExceptionCategory(exception));
        }
        try
        {
            using var errorScope = _scopeFactory.CreateScope();
            var auditEvents = errorScope.ServiceProvider.GetRequiredService<IAuditEventService>();
            await auditEvents.RecordAsync(
                workItem.ActorUserId,
                GetAuditEventType(workItem.ImportType, AuditOutcomes.Failed),
                exception is ImportOutcomeUnknownException ? AuditOutcomes.Unknown : exception is OperationCanceledException ? AuditOutcomes.Cancelled : AuditOutcomes.Failed,
                cancellationToken: cancellationToken);
            var progress = errorScope.ServiceProvider.GetRequiredService<IImportProgressService>();
            progress.AddErrors(workItem.ProgressId, new[] { errorMessage });
            progress.SetStatus(workItem.ProgressId, errorMessage);
            progress.Complete(workItem.ProgressId);
        }
        catch (Exception reportingException)
        {
            _logger.LogError("Could not report failed import {ProgressId}. ExceptionType={ExceptionType}",
                workItem.ProgressId, ImportDiagnosticMessages.ExceptionCategory(reportingException));
        }
    }

    private static string GetAuditEventType(string importType, string outcome)
        => string.Equals(importType, "donations", StringComparison.OrdinalIgnoreCase)
            ? outcome == AuditOutcomes.Succeeded ? AuditEventTypes.DonationImportCompleted : AuditEventTypes.DonationImportFailed
            : outcome == AuditOutcomes.Succeeded ? AuditEventTypes.AccountingImportCompleted : AuditEventTypes.AccountingImportFailed;
}