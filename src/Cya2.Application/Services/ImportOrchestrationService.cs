using System.Collections.Concurrent;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
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

    public ImportOrchestrationService(
        IEnumerable<IImportProcessor> processors,
        IImportProgressService progressService,
        IImportWorkQueue workQueue,
        ImportPreviewStore previews,
        ImportUploadValidator uploadValidator,
        ILogger<ImportOrchestrationService> logger)
    {
        _processors = processors.ToDictionary(p => p.ImportType, StringComparer.OrdinalIgnoreCase);
        _progressService = progressService;
        _workQueue = workQueue;
        _previews = previews;
        _uploadValidator = uploadValidator;
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

        var data = await _uploadValidator.ReadAndValidateAsync(file, fileName, cancellationToken);

        var previewId = Guid.NewGuid().ToString("N");
        if (!_previews.TrySet(previewId, new ImportPreview(data, fileName ?? string.Empty, contentType ?? string.Empty, DateTime.UtcNow)))
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

        if (!_previews.TryRemove(previewId, out var preview) || preview is null)
        {
            var expired = new ImportResult();
            expired.Errors.Add("Preview session expired. Please upload the file again.");
            return expired;
        }

        var progressId = Guid.NewGuid().ToString("N");
        _progressService.Start(progressId, importType);
        await using var stream = new MemoryStream(preview.Data, writable: false);
        return await GetProcessor(importType).ProcessAsync(stream, progressId, cancellationToken);
    }

    public async Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null)
    {
        var result = new ImportResult { ProgressId = Guid.NewGuid().ToString("N") };
        _progressService.Start(result.ProgressId, importType);

        if (string.IsNullOrWhiteSpace(previewId))
        {
            result.Errors.Add("PreviewId is required");
            _progressService.SetStatus(result.ProgressId, "PreviewId is required");
            return result;
        }

        if (!_previews.TryRemove(previewId, out var preview) || preview is null)
        {
            result.Errors.Add("Preview session expired. Please upload the file again.");
            _progressService.SetStatus(result.ProgressId, "Preview session expired");
            return result;
        }

        var id = result.ProgressId;
        _logger.LogInformation("Queueing background {ImportType} import. PreviewId={PreviewId}, ProgressId={ProgressId}", importType, previewId, id);
        await _workQueue.EnqueueAsync(new ImportWorkItem(importType, previewId, id, preview.Data));

        return result;
    }

    private IImportProcessor GetProcessor(string importType)
        => _processors.TryGetValue(importType, out var processor)
            ? processor
            : throw new ArgumentException($"Invalid import type: {importType}", nameof(importType));
}
