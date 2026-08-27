using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cya2.Application.Interfaces;

public sealed class FilePreviewResult
{
    public string PreviewId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public sealed class ImportResult
{
    public int TotalRows { get; set; }
    public int InsertedRows { get; set; }
    public int FailedRows { get; set; }
    public List<string> Errors { get; } = new();
    public string? ProgressId { get; set; }
}

public interface IImportProcessor
{
    string ImportType { get; }
    Task<ImportResult> ProcessAsync(Stream file, string progressId, CancellationToken cancellationToken);
}

public interface IImportOrchestrationService
{
    Task<FilePreviewResult> PreviewAsync(Stream file, string importType, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportFromPreviewAsync(string previewId, string importType, CancellationToken cancellationToken = default);
    Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null);
}
