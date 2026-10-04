namespace Cya2.Application.Services;

public sealed class ImportUploadOptions
{
    public const string SectionName = "Import";
    public const long DefaultMaximumUploadBytes = 5_000L * 1024;

    public long MaximumUploadBytes { get; set; } = DefaultMaximumUploadBytes;
}