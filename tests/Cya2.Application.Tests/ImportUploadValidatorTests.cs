using Cya2.Application.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ImportUploadValidatorTests
{
    private const long MaximumUploadBytes = 5_000L * 1024;

    [Fact]
    public async Task RejectsFileLargerThanConfiguredLimit()
    {
        var validator = CreateValidator();
        await using var stream = new MemoryStream(new byte[MaximumUploadBytes + 1]);

        var exception = await Assert.ThrowsAsync<ImportUploadValidationException>(() =>
            validator.ReadAndValidateAsync(stream, "large.xlsx"));

        Assert.Contains("5,000 KB", exception.Message);
    }

    [Fact]
    public async Task RejectsWrongExtension()
    {
        var validator = CreateValidator();
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        var exception = await Assert.ThrowsAsync<ImportUploadValidationException>(() =>
            validator.ReadAndValidateAsync(stream, "workbook.xls"));

        Assert.Contains(".xlsx", exception.Message);
    }

    [Fact]
    public async Task RejectsCorruptXlsxPayload()
    {
        var validator = CreateValidator();
        await using var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3]);

        var exception = await Assert.ThrowsAsync<ImportUploadValidationException>(() =>
            validator.ReadAndValidateAsync(stream, "corrupt.xlsx"));

        Assert.Contains("valid Excel workbook", exception.Message);
    }

    [Fact]
    public async Task AcceptsValidMinimalOoxmlPackage()
    {
        var validator = CreateValidator();
        await using var stream = CreateMinimalOoxmlPackage();

        var result = await validator.ReadAndValidateAsync(stream, "valid.xlsx");

        Assert.NotEmpty(result);
    }

    private static ImportUploadValidator CreateValidator()
        => new(Options.Create(new ImportUploadOptions { MaximumUploadBytes = MaximumUploadBytes }));

    private static MemoryStream CreateMinimalOoxmlPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            AddEntry(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
