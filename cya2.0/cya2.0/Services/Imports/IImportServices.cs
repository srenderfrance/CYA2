using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace cya2.Services.Imports
{
    public sealed class FilePreviewResult
    {
        public string PreviewId { get; set; } = string.Empty; // server-side token
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = string.Empty; // e.g. application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
    }

    public sealed class ImportRequest
    {
        public string PreviewId { get; set; } = string.Empty;
        public string ProgressId { get; set; } = string.Empty;
        public string ImportType { get; set; } = string.Empty;
    }

    public interface IAccountingImportService
    {
        Task<ImportResult> ImportAsync(Stream file, CancellationToken ct);
        Task<ImportResult> StartImportAsync(Stream file, CancellationToken ct);

        // New: generate a lightweight preview before import
        Task<FilePreviewResult> PreviewAsync(Stream file, string fileName, string contentType, CancellationToken ct);
        Task<ImportResult> ImportFromPreviewAsync(string previewId, CancellationToken ct);
        // Start import from an existing preview and run processing in background, returning ProgressId immediately
        Task<ImportResult> StartImportFromPreviewAsync(string previewId, string progressId);
    }

    public interface IDonationImportService
    {
        Task<ImportResult> ImportAsync(Stream file, CancellationToken ct);
        Task<ImportResult> StartImportAsync(Stream file, CancellationToken ct);

        // New: generate a lightweight preview before import
        Task<FilePreviewResult> PreviewAsync(Stream file, string fileName, string contentType, CancellationToken ct);
        Task<ImportResult> ImportFromPreviewAsync(string previewId, CancellationToken ct);
        // Start import from an existing preview and run processing in background, returning ProgressId immediately
        Task<ImportResult> StartImportFromPreviewAsync(string previewId, string progressId);
    }
}
