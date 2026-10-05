using Cya2.Application.Services;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
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
    public async Task RejectsThirdConcurrentUploadValidation()
    {
        var validator = CreateValidator(maximumConcurrentUploads: 2);
        using var first = new BlockingReadStream();
        using var second = new BlockingReadStream();
        using var third = new MemoryStream([0x50, 0x4B, 0x03, 0x04]);

        var firstTask = validator.ReadAndValidateAsync(first, "one.xlsx");
        var secondTask = validator.ReadAndValidateAsync(second, "two.xlsx");

        await Task.WhenAll(first.WaitUntilReadAsync(), second.WaitUntilReadAsync());
        var exception = await Assert.ThrowsAsync<ImportUploadCapacityException>(() =>
            validator.ReadAndValidateAsync(third, "three.xlsx"));

        Assert.Contains("simultaneous uploads", exception.Message);
        first.Release();
        second.Release();
        await Assert.ThrowsAnyAsync<Exception>(() => firstTask);
        await Assert.ThrowsAnyAsync<Exception>(() => secondTask);
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
    public async Task RejectsOoxmlLookingPackageThatCannotBeOpened()
    {
        var validator = CreateValidator();
        await using var stream = CreateOoxmlLookingPackage(
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />",
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><broken>");

        var exception = await Assert.ThrowsAsync<ImportUploadValidationException>(() =>
            validator.ReadAndValidateAsync(stream, "malformed.xlsx"));

        Assert.Contains("valid Excel workbook", exception.Message);
    }

    [Fact]
    public async Task RejectsWorkbookWithoutWorksheets()
    {
        var validator = CreateValidator();
        await using var stream = CreateWorkbookWithoutWorksheets();

        var exception = await Assert.ThrowsAsync<ImportUploadValidationException>(() =>
            validator.ReadAndValidateAsync(stream, "empty.xlsx"));

        Assert.Contains("valid Excel workbook", exception.Message);
    }

    [Fact]
    public async Task AcceptsValidMinimalOoxmlPackage()
    {
        var validator = CreateValidator();
        await using var stream = CreateValidWorkbook();

        var result = await validator.ReadAndValidateAsync(stream, "valid.xlsx");

        Assert.NotEmpty(result);
    }

    private static ImportUploadValidator CreateValidator(int maximumConcurrentUploads = 2)
        => new(Options.Create(new ImportUploadOptions
        {
            MaximumUploadBytes = MaximumUploadBytes,
            MaximumConcurrentUploads = maximumConcurrentUploads
        }));

    private static MemoryStream CreateValidWorkbook()
    {
        var stream = new MemoryStream();
        ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");
        using (var package = new ExcelPackage())
        {
            package.Workbook.Worksheets.Add("Sheet1").Cells["A1"].Value = "Valid";
            package.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateWorkbookWithoutWorksheets()
    {
        using var validWorkbook = CreateValidWorkbook();
        using var sourceArchive = new System.IO.Compression.ZipArchive(
            validWorkbook,
            System.IO.Compression.ZipArchiveMode.Read,
            leaveOpen: true);
        var stream = new MemoryStream();
        using (var targetArchive = new System.IO.Compression.ZipArchive(
                   stream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var sourceEntry in sourceArchive.Entries)
            {
                var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName);
                using var source = sourceEntry.Open();
                using var target = targetEntry.Open();
                if (string.Equals(sourceEntry.FullName, "xl/workbook.xml", StringComparison.Ordinal))
                {
                    var workbookXml = System.Xml.Linq.XDocument.Load(source);
                    workbookXml.Root?.Element(workbookXml.Root.GetDefaultNamespace() + "sheets")?.RemoveNodes();
                    workbookXml.Save(target);
                }
                else
                {
                    source.CopyTo(target);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateOoxmlLookingPackage(string contentTypes, string workbook)
    {
        var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
                   stream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypes);
            AddEntry(archive, "xl/workbook.xml", workbook);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilReadAsync() => _readStarted.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
