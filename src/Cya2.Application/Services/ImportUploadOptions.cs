namespace Cya2.Application.Services;

public sealed class ImportUploadOptions
{
    public const string SectionName = "Import";
    public const long DefaultMaximumUploadBytes = 5_000L * 1024;
    public const int DefaultMaximumConcurrentUploads = 2;
    public const int DefaultMaximumRetainedPreviews = 2;

    public long MaximumUploadBytes { get; set; } = DefaultMaximumUploadBytes;
    public int MaximumConcurrentUploads { get; set; } = DefaultMaximumConcurrentUploads;
    public int MaximumRetainedPreviews { get; set; } = DefaultMaximumRetainedPreviews;
    public long MaximumRetainedPreviewBytes { get; set; } = DefaultMaximumUploadBytes * DefaultMaximumRetainedPreviews;
}