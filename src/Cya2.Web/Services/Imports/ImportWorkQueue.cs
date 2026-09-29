using System.Threading.Channels;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                    await using var stream = new MemoryStream(workItem.Data, writable: false);
                    _logger.LogInformation("Background {ImportType} processor started. PreviewId={PreviewId}, ProgressId={ProgressId}", workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                    await processor.ProcessAsync(stream, workItem.ProgressId, stoppingToken);
                    _logger.LogInformation("Background {ImportType} processor returned. PreviewId={PreviewId}, ProgressId={ProgressId}", workItem.ImportType, workItem.PreviewId, workItem.ProgressId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ReportFailure(workItem, ex);
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

    private void ReportFailure(ImportWorkItem workItem, Exception exception)
    {
        var errorMessage = $"Import failed: {exception.Message}";
        _logger.LogError(exception, "Background {ImportType} import failed for preview {PreviewId}", workItem.ImportType, workItem.PreviewId);
        try
        {
            using var errorScope = _scopeFactory.CreateScope();
            var progress = errorScope.ServiceProvider.GetRequiredService<IImportProgressService>();
            progress.AddErrors(workItem.ProgressId, new[] { errorMessage });
            progress.SetStatus(workItem.ProgressId, errorMessage);
            progress.Complete(workItem.ProgressId);
        }
        catch (Exception reportingException)
        {
            _logger.LogError(reportingException, "Could not report failed import {ProgressId}", workItem.ProgressId);
        }
    }
}