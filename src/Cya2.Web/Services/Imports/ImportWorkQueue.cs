using System.Threading.Channels;
using Cya2.Application.Interfaces;
using Cya2.Application.Diagnostics;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

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
        _logger.LogInformation("Import work queue worker started.");
        try
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
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
                    await auditEvents.RecordAsync(
                        workItem.ActorUserId,
                        GetAuditEventType(workItem.ImportType, importResult.FailedRows > 0 ? AuditOutcomes.Failed : AuditOutcomes.Succeeded),
                        importResult.FailedRows > 0 ? AuditOutcomes.Failed : AuditOutcomes.Succeeded,
                        itemCount: importResult.InsertedRows,
                        cancellationToken: stoppingToken);
                    _logger.LogInformation("Background {ImportType} processor returned. PreviewId={PreviewId}, ProgressId={ProgressId}", workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await ReportFailureAsync(workItem, ex, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Import work queue worker stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Import work queue worker stopped unexpectedly.");
            throw;
        }
    }

    private async Task ReportFailureAsync(ImportWorkItem workItem, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = ImportDiagnosticMessages.ImportFailed();
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
                AuditOutcomes.Failed,
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