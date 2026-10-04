using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace Cya2.Application.Services;

public sealed class ImportUploadValidationException : System.Exception
{
    public ImportUploadValidationException(string message)
        : base(message)
    {
    }
}

public sealed class ImportUploadValidator
{
    private const int ZipSignatureLength = 4;
    private readonly IOptions<ImportUploadOptions> _options;

    public ImportUploadValidator(IOptions<ImportUploadOptions> options)
    {
        _options = options;
    }

    public async Task<byte[]> ReadAndValidateAsync(
        Stream file,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var maximumBytes = _options.Value.MaximumUploadBytes;
        if (maximumBytes <= 0)
            throw new InvalidOperationException("The maximum import upload size must be greater than zero.");

        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ImportUploadValidationException("The selected file must be an .xlsx workbook.");

        await using var boundedStream = new MemoryStream();
        var buffer = new byte[81920];
        var totalBytes = 0L;

        while (true)
        {
            var read = await file.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;

            totalBytes += read;
            if (totalBytes > maximumBytes)
                throw new ImportUploadValidationException(
                    $"The file exceeds the maximum allowed size of {maximumBytes / 1024:N0} KB.");

            await boundedStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (totalBytes < ZipSignatureLength)
            throw new ImportUploadValidationException("The uploaded file is not a valid Excel workbook.");

        var data = boundedStream.ToArray();
        if (!HasZipSignature(data) || !HasExcelPackageEntries(data))
            throw new ImportUploadValidationException("The uploaded file is not a valid Excel workbook.");

        return data;
    }

    private static bool HasZipSignature(byte[] data)
        => data[0] == 0x50 && data[1] == 0x4B &&
           ((data[2] == 0x03 && data[3] == 0x04) ||
            (data[2] == 0x05 && data[3] == 0x06) ||
            (data[2] == 0x07 && data[3] == 0x08));

    private static bool HasExcelPackageEntries(byte[] data)
    {
        try
        {
            using var package = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
            return package.GetEntry("[Content_Types].xml") is not null &&
                   package.GetEntry("xl/workbook.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}