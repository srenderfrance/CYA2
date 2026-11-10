using Cya2.Application.Interfaces;
using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using Xunit;

namespace Cya2.Database.Tests;

public sealed class ImportValidationTests
{
    private static readonly string[] DonationHeaders = ["Gift Date", "Name", "Gift Payment Type", "Gift Type", "Fund Split Amount", "Fund Notes", "Soft Credit Recipient Name", "Preferred Address Line 1", "Preferred City", "Preferred State", "Preferred ZIP", "Preferred Country", "Personal Email Number", "Home Phone Number", "Personal Mobile Phone Number", "Gift Is Anonymous", "Gift Import ID", "Primary Addressee", "Honor/Memorial Name", "Constituent ID"];
    private static readonly string[] AccountingHeaders = ["Class", "Date", "Num", "Amount", "Account #", "Account", "Type"];

    [Fact]
    public async Task ConstituentColumnIsRequiredButBlankValuesAreAllowed()
    {
        var repository = new Repository();
        using var source = Workbook("donations");
        using var package = new ExcelPackage(source);
        package.Workbook.Worksheets[0].Cells[1, DonationHeaders.Length].Value = "Other";
        using var workbook = new MemoryStream(package.GetAsByteArray());
        var result = await Processor("donations", repository, new Invalidator()).ProcessAsync(workbook, "test", default);
        Assert.Contains("Missing column: Constituent ID", result.Errors);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task ConstituentIdsPreserveZerosAndSoftCreditContactsAreExcluded()
    {
        var repository = new Repository();
        using var source = Workbook("donations");
        using var package = new ExcelPackage(source);
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[2, DonationHeaders.Length].Value = "000123";
        sheet.Cells[3, DonationHeaders.Length].Value = "organization-1";
        sheet.Cells[3, 2].Value = "Employer Organization";
        sheet.Cells[3, 18].Value = "Employer Organization";
        sheet.Cells[3, 7].Value = "Synthetic Donor";
        sheet.Cells[3, 13].Value = "organization@example.invalid";
        sheet.Cells[3, 17].Value = sheet.Cells[2, 17].Value;
        using var workbook = new MemoryStream(package.GetAsByteArray());
        var result = await Processor("donations", repository, new Invalidator()).ProcessAsync(workbook, "test", default);
        Assert.Empty(result.Errors);
        Assert.Equal(2, repository.Published.Count);
        Assert.Equal("000123", repository.Published[0].ConstituentId);
        Assert.Equal("organization-1", repository.Published[1].ConstituentId);
        Assert.All(repository.Published, row => Assert.Equal("id:000123", row.Donor.IdentityKey));
        Assert.True(repository.Published[1].IsSoftCredit);
        Assert.Empty(repository.Published[1].Contacts);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task PopulatedMalformedRowRejectsEntireUpload(string kind)
    {
        var repository = new Repository();
        using var source = Workbook(kind);
        using var package = new ExcelPackage(source);
        var row = kind == "donations" ? 3 : 7;
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[row, 1, row, sheet.Dimension.End.Column].Clear();
        sheet.Cells[row, kind == "donations" ? 1 : 2].Value = "01/15/2026";
        using var workbook = new MemoryStream(package.GetAsByteArray());
        var result = await Processor(kind, repository, new Invalidator()).ProcessAsync(workbook, "test", default);
        Assert.NotEmpty(result.Errors);
        Assert.False(result.PublicationCommitted);
        Assert.Equal(0, repository.Writes);
    }

    [Theory]
    [InlineData("donations", true)]
    [InlineData("accounting", true)]
    [InlineData("donations", false)]
    [InlineData("accounting", false)]
    public async Task NotificationFailurePreservesCommittedResult(string kind, bool cacheFailure)
    {
        var repository = new Repository();
        var cache = new Invalidator { Throw = cacheFailure };
        var progress = new Progress { ThrowOnComplete = !cacheFailure };
        using var workbook = Workbook(kind);
        var result = await Processor(kind, repository, cache, progress).ProcessAsync(workbook, "test", default);
        Assert.True(result.PublicationCommitted);
        Assert.Equal(2, result.InsertedRows);
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(1, repository.Writes);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task CacheFailureDoesNotMaskUnknownOutcome(string kind)
    {
        using var workbook = Workbook(kind);
        await Assert.ThrowsAsync<ImportOutcomeUnknownException>(() => Processor(kind,
            new Repository { UnknownOutcome = true }, new Invalidator { Throw = true }).ProcessAsync(workbook, "test", default));
    }

    [Fact]
    public async Task FrequencyHistoryMatchesResolvedIdentityNotSourceName()
    {
        var repository = new Repository();
        repository.HistoryFactory = keys =>
        {
            var key = keys.Single();
            return Enumerable.Range(1, 12).Select(month => new DonationRecord
            {
                DonorIdentityKey = key.IdentityKey, Fund = key.Fund, AccountName = "Different source name",
                Date = new DateTime(2026, 1, 15).AddMonths(-month), Amount = 10
            }).ToList();
        };
        using var workbook = Workbook("donations");
        await Processor("donations", repository, new Invalidator()).ProcessAsync(workbook, "test", default);
        Assert.All(repository.Published, row => Assert.Equal(Cya2.Core.Enums.DonorFrequency.Monthly, row.Frequency));
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task InvalidRowRejectsWholeWorkbookWithoutRepositoryWrites(string kind)
    {
        var repository = new Repository();
        var cache = new Invalidator();
        using var workbook = Workbook(kind, invalid: true);
        var result = await Processor(kind, repository, cache).ProcessAsync(workbook, "test", default);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.InsertedRows);
        Assert.Equal(0, repository.Writes);
        Assert.Equal(CacheDataDomains.None, cache.Domains);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task ValidWorkbookPublishesOnceAndInvalidatesCommittedDomains(string kind)
    {
        var repository = new Repository();
        var cache = new Invalidator();
        using var workbook = Workbook(kind);
        var result = await Processor(kind, repository, cache).ProcessAsync(workbook, "test", default);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.InsertedRows);
        Assert.Equal(1, repository.Writes);
        Assert.Equal(kind == "donations" ? CacheDataDomains.Donations | CacheDataDomains.DonorContacts : CacheDataDomains.Accounting, cache.Domains);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task EmptyWorkbookRejectsWithoutWrites(string kind)
    {
        var repository = new Repository();
        using var source = Workbook(kind);
        using var package = new ExcelPackage(source);
        var firstDataRow = kind == "donations" ? 2 : 6;
        package.Workbook.Worksheets[0].DeleteRow(firstDataRow, 2);
        using var empty = new MemoryStream(package.GetAsByteArray());
        var result = await Processor(kind, repository, new Invalidator()).ProcessAsync(empty, "test", default);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, repository.Writes);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task UnknownCommitOutcomeInvalidatesAffectedDomains(string kind)
    {
        var repository = new Repository { UnknownOutcome = true };
        var cache = new Invalidator();
        using var workbook = Workbook(kind);
        await Assert.ThrowsAsync<ImportOutcomeUnknownException>(() => Processor(kind, repository, cache).ProcessAsync(workbook, "test", default));
        Assert.Equal(kind == "donations" ? CacheDataDomains.Donations | CacheDataDomains.DonorContacts : CacheDataDomains.Accounting, cache.Domains);
    }

    [Theory]
    [InlineData("donations")]
    [InlineData("accounting")]
    public async Task CancelledWorkbookDoesNotPublish(string kind)
    {
        var repository = new Repository();
        using var workbook = Workbook(kind);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(kind, repository, new Invalidator()).ProcessAsync(workbook, "test", cancellation.Token));
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task DifferentProcessorsShareOneWriteGate()
    {
        var repository = new Repository { BlockWrites = true };
        using var donation = Workbook("donations");
        using var accounting = Workbook("accounting");
        var first = Processor("donations", repository, new Invalidator()).ProcessAsync(donation, "first", default);
        await repository.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = Processor("accounting", repository, new Invalidator()).ProcessAsync(accounting, "second", cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal(1, repository.Writes);
        }
        finally { repository.Release.TrySetResult(); }
        await first;
    }

    private static IImportProcessor Processor(string kind, Repository repository, Invalidator cache, Progress? progress = null)
    {
        var config = new ConfigurationBuilder().Build();
        return kind == "donations"
            ? new DonationImportProcessor(config, NullLogger<DonationImportProcessor>.Instance, progress ?? new Progress(), repository, cache, new DonorFrequencyService(), new DonorIdentityResolver())
            : new AccountingImportProcessor(config, NullLogger<AccountingImportProcessor>.Instance, progress ?? new Progress(), repository, cache);
    }

    private static MemoryStream Workbook(string kind, bool invalid = false)
    {
        ExcelPackage.License.SetNonCommercialOrganization("Synthetic integration tests");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Synthetic");
        var headers = kind == "donations" ? DonationHeaders : AccountingHeaders;
        var headerRow = kind == "donations" ? 1 : 5;
        for (var c = 0; c < headers.Length; c++) sheet.Cells[headerRow, c + 1].Value = headers[c];
        for (var row = 1; row <= 2; row++)
        {
            for (var c = 0; c < headers.Length; c++)
                sheet.Cells[headerRow + row, c + 1].Value = headers[c] switch
                {
                    "Gift Date" or "Date" => invalid && row == 2 ? "invalid" : "01/15/2026",
                    "Fund Split Amount" or "Amount" => "10.00",
                    "Name" or "Primary Addressee" => "Synthetic Donor",
                    "Fund Notes" => "Synthetic Fund",
                    "Gift Import ID" => $"synthetic-{row}",
                    "Class" => "Synthetic Class",
                    "Account" => "Synthetic Account",
                    "Account #" => "1000",
                    "Gift Payment Type" => "Check",
                    "Gift Type" => "Donation",
                    _ => string.Empty
                };
        }
        return new MemoryStream(package.GetAsByteArray());
    }

    private sealed class Repository : IDonationRepository, IAccountingImportRepository
    {
        public int Writes;
        public bool BlockWrites;
        public bool UnknownOutcome;
        public IReadOnlyList<CanonicalDonationWriteModel> Published = [];
        public Func<IEnumerable<(string IdentityKey, string Fund)>, List<DonationRecord>>? HistoryFactory;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Record(CancellationToken ct)
        {
            Interlocked.Increment(ref Writes);
            Entered.TrySetResult();
            if (BlockWrites) await Release.Task.WaitAsync(ct);
            if (UnknownOutcome) throw new ImportOutcomeUnknownException();
        }
        public async Task<DonationImportPersistenceResult> ReplaceCanonicalDonationsAsync(IReadOnlyList<CanonicalDonationWriteModel> rows, CancellationToken ct)
        {
            await Record(ct);
            Published = rows;
            return new() { Inserted = rows.Count };
        }
        public async Task<ImportBatchResult> ReplaceAsync(IReadOnlyList<AccountingImportRowDto> rows, CancellationToken ct)
        {
            await Record(ct);
            return new() { Inserted = rows.Count };
        }
        public Task<List<DonationRecord>> GetRecentDonationsForDonorsAsync(IEnumerable<(string IdentityKey, string Fund)> donorKeys, DateTime beforeDate, int maxPerDonor, CancellationToken ct) => Task.FromResult(HistoryFactory?.Invoke(donorKeys) ?? []);
        public Task<int> RecategorizeAllDonationsAsync(CancellationToken ct) => Task.FromResult(0);
    }
    private sealed class Invalidator : IImportCacheInvalidator
    {
        public CacheDataDomains Domains;
        public bool Throw;
        public void InvalidateAll() => Domains = CacheDataDomains.All;
        public void Invalidate(CacheDataDomains domains)
        {
            if (Throw) throw new InvalidOperationException("Injected cache failure");
            Domains |= domains;
        }
    }
    private sealed class Progress : IImportProgressService
    {
        public bool ThrowOnComplete;
        public void Start(string id, string importType, string ownerUserId) { }
        public void AddStep(string id, string stepName, string status = "Starting...") { }
        public void UpdateStep(string id, string stepName, string status, string? details = null) { }
        public void CompleteStep(string id, string stepName, string completionStatus, string? details = null) { }
        public void Report(string id, int totalRows, int insertedRows, int failedRows, string? status = null) { }
        public void SetExpected(string id, int expectedRows) { }
        public void SetStatus(string id, string status) { }
        public void AddErrors(string id, IEnumerable<string> errors) { }
        public void Complete(string id)
        {
            if (ThrowOnComplete) throw new InvalidOperationException("Injected progress failure");
        }
    }
}
