using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Repositories;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Xunit;
using static Cya2.Database.Tests.MySqlFixture;

namespace Cya2.Database.Tests;

public sealed class DatabaseTests(MySqlFixture mysql) : IClassFixture<MySqlFixture>
{
    private static readonly DateTime Date = new(2026, 1, 15);
    private static readonly AvailableDatabase Guard = new();

    [MySqlFact]
    public async Task FreshSchema_MigrationsAreAppliedOnce_AndEditedBaselineIsRejected()
    {
        var db = await mysql.CreateDatabaseAsync();
        var expected = 1 + Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "database", "migrations"), "*.sql").Length;
        Assert.Equal(expected.ToString(), await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM Cya2SchemaMigrations"));
        Assert.Equal("2", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('DonationBackupSnapshots', 'AccountingBackupSnapshots') AND column_name='BackupAt' AND datetime_precision=6"));
        var rerun = await MigrateAsync(db.SetupConnection);
        Assert.True(rerun.ExitCode == 0, rerun.Output);
        Assert.Equal(expected.ToString(), await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM Cya2SchemaMigrations"));
        Assert.Equal("1", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='AuditEvents'"));
        var altered = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(altered, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "schema", "001_InitialSchema.sql")) + "\n-- checksum test\n");
            var rejected = await MigrateAsync(db.SetupConnection, baselinePath: altered);
            Assert.Equal(1, rejected.ExitCode);
            Assert.Contains("checksum", rejected.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(altered); }
    }

    [MySqlFact]
    public async Task RuntimeIdentity_CanUseTemporaryTables_ButCannotManagePersistentSchema()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.RuntimeConnection, "CREATE TEMPORARY TABLE TestStage (Id INT); INSERT INTO TestStage VALUES (1); DROP TEMPORARY TABLE TestStage");
        foreach (var sql in new[]
        {
            "CREATE TABLE ForbiddenTable (Id INT)",
            "ALTER TABLE DonationData ADD ForbiddenColumn INT",
            "DROP TABLE DonationData",
            "GRANT ALL ON *.* TO CURRENT_USER()"
        })
        {
            var error = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(db.RuntimeConnection, sql));
            Assert.Contains(error.Number, new[] { 1044, 1045, 1142, 1227 });
        }
        var result = await Donations(db).ReplaceCanonicalDonationsAsync([Donation()], default);
        Assert.Equal(1, result.Inserted);
    }

    [MySqlFact]
    public async Task DonationPersistence_ReconcilesCurrentContacts_PreservingRetainedContactCreation()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("keep@example.invalid"), Email("old@example.invalid")])], default);
        var created = await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE ContactValue='keep@example.invalid'");
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("keep@example.invalid"), Email("new@example.invalid")])], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='old@example.invalid'"));
        Assert.Equal(created, await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE ContactValue='keep@example.invalid'"));
        var reader = new DonationReadRepository(Config(db), Guard, NullLogger<DonationReadRepository>.Instance);
        var rows = await reader.GetDonationsByFundsAndDateRangeAsync(["Test Fund"], Date.AddDays(-1), Date.AddDays(1));
        Assert.Single(rows);
    }

    [MySqlFact]
    public async Task ContactWriteFailure_RollsBackDonorDonationAndContactChanges()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "CREATE TRIGGER FailContact BEFORE INSERT ON DonorContacts FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected integration test failure'");
        var error = await Assert.ThrowsAsync<MySqlException>(() => Donations(db).ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("test@example.invalid")])], default));
        Assert.Equal(1644, error.Number);
        foreach (var table in new[] { "Donors", "DonationData", "DonorContacts" })
            Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, $"SELECT COUNT(*) FROM {table}"));
    }

    [MySqlFact]
    public async Task DonationRollback_RestoresSnapshots_AndRemovesNewOrphanDonors()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("original@example.invalid")])], default);
        await repository.ReplaceCanonicalDonationsAsync([
            Donation(amount: 99, contacts: [Email("replacement@example.invalid")]),
            Donation(identity: "newdonor", amount: 25)
        ], default);
        var result = await Rollback(db).RollbackDonationsAsync();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
        Assert.Equal("10.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM DonationData"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors"));
        Assert.Equal("original@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationBackupSnapshots WHERE Pinned=1"));
    }

    [MySqlFact]
    public async Task AccountingImportAndRollback_RestoreOriginalRows_IncludingEarlierRange()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = new AccountingImportRepository(Config(db), NullLogger<AccountingImportRepository>.Instance, new NoProgress(), Guard);
        var initial = await repository.ReplaceAsync([Accounting(Date.AddMonths(-1), 5), Accounting(Date, 10)], default);
        Assert.Equal(2, initial.Inserted);
        Assert.Empty(initial.Errors);
        var replacement = await repository.ReplaceAsync([Accounting(Date, 99)], default);
        Assert.Equal(1, replacement.Inserted);
        Assert.Empty(replacement.Errors);
        var result = await Rollback(db).RollbackAccountingAsync();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountingData"));
        Assert.Equal("15.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(SUM(Amount) AS CHAR) FROM AccountingData"));
    }

    [MySqlFact]
    public async Task RollbackWithoutSnapshot_FailsWithoutChangingData()
    {
        var db = await mysql.CreateDatabaseAsync();
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation()], default);
        await ExecuteAsync(db.SetupConnection, "DELETE FROM DonationBackupSnapshots; DELETE FROM DonationDataBackup; DELETE FROM DonorContactsBackup; DELETE FROM DonorsBackup");
        var result = await Rollback(db).RollbackDonationsAsync();
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
    }

    [MySqlFact]
    public async Task FailedReplacementPreservesExistingDonationAndSnapshot()
    {
        var db = await mysql.CreateDatabaseAsync();
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("original@example.invalid")])], default);
        var backup = await ScalarAsync(db.RuntimeConnection, "SELECT BackupId FROM DonationBackupSnapshots LIMIT 1");
        await ExecuteAsync(db.SetupConnection, "CREATE TRIGGER FailContact BEFORE INSERT ON DonorContacts FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected failure'");
        await Assert.ThrowsAsync<MySqlException>(() => Donations(db).ReplaceCanonicalDonationsAsync([Donation(amount: 99, contacts: [Email("changed@example.invalid")])], default));
        Assert.Equal("10.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM DonationData"));
        Assert.Equal("original@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
        Assert.Equal(backup, await ScalarAsync(db.RuntimeConnection, "SELECT BackupId FROM DonationBackupSnapshots LIMIT 1"));
    }

    [MySqlFact]
    public async Task RollbackRestoresTouchedDonorWithOnlyOlderHistory()
    {
        var db = await mysql.CreateDatabaseAsync();
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation(date: Date.AddMonths(-2), contacts: [Email("original@example.invalid")])], default);
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("changed@example.invalid")])], default);
        var result = await Rollback(db).RollbackDonationsAsync();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
        Assert.Equal("original@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
    }

    [MySqlFact]
    public async Task AccountingPublicationFailureRollsBackDeletionAndSnapshot()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = new AccountingImportRepository(Config(db), NullLogger<AccountingImportRepository>.Instance, new NoProgress(), Guard);
        await repository.ReplaceAsync([Accounting(Date, 10)], default);
        var backup = await ScalarAsync(db.RuntimeConnection, "SELECT BackupId FROM AccountingBackupSnapshots LIMIT 1");
        await ExecuteAsync(db.SetupConnection, "CREATE TRIGGER FailAccounting BEFORE INSERT ON AccountingData FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected failure'");
        await Assert.ThrowsAsync<MySqlException>(() => repository.ReplaceAsync([Accounting(Date, 99)], default));
        Assert.Equal("10.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM AccountingData"));
        Assert.Equal(backup, await ScalarAsync(db.RuntimeConnection, "SELECT BackupId FROM AccountingBackupSnapshots LIMIT 1"));
    }

    [MySqlFact]
    public async Task PreCancelledReplacementPreservesData()
    {
        var db = await mysql.CreateDatabaseAsync();
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation()], default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Donations(db).ReplaceCanonicalDonationsAsync([Donation(amount: 99)], cancellation.Token));
        Assert.Equal("10.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM DonationData"));
    }

    private static IConfiguration Config(TestDatabase db) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:default"] = db.RuntimeConnection }).Build();
    private static DonationRepository Donations(TestDatabase db) => new(Config(db), Guard, NullLogger<DonationRepository>.Instance);
    private static RollbackExecutor Rollback(TestDatabase db) => new(Config(db), NullLogger<RollbackExecutor>.Instance,
        new RollbackRepository(Config(db), NullLogger<RollbackRepository>.Instance, Guard));
    private static CanonicalDonationWriteModel Donation(string identity = "testdonor", decimal amount = 10,
        IReadOnlyList<DonorContactWriteModel>? contacts = null, DateTime? date = null) => new()
    {
        Donor = new DonorWriteModel { Fund = "Test Fund", DisplayName = identity, IdentityKey = identity, ResolutionSource = "AccountName", ResolutionVersion = 1 },
        Date = date ?? Date, AccountName = identity, PaymentMethod = "Check", GiftType = "Donation", Amount = amount, Fund = "Test Fund", Contacts = contacts ?? []
    };
    private static DonorContactWriteModel Email(string value) => new() { ContactType = "Email", ContactValue = value, NormalizedValue = value };
    private static AccountingImportRowDto Accounting(DateTime date, double amount) => new()
    {
        AccountingClass = "Test Class", Date = date, Num = "TEST", Amount = amount,
        AccountNumber = "1000", Account = "Test Account", Type = "Deposit"
    };
    private sealed class AvailableDatabase : IDatabaseGuard
    {
        public bool IsAvailable => true;
        public void ThrowIfUnavailable() { }
    }
    private sealed class NoProgress : IImportProgressService
    {
        public void Start(string id, string importType, string ownerUserId) { }
        public void AddStep(string id, string stepName, string status = "Starting...") { }
        public void UpdateStep(string id, string stepName, string status, string? details = null) { }
        public void CompleteStep(string id, string stepName, string completionStatus, string? details = null) { }
        public void Report(string id, int totalRows, int insertedRows, int failedRows, string? status = null) { }
        public void SetExpected(string id, int expectedRows) { }
        public void SetStatus(string id, string status) { }
        public void AddErrors(string id, IEnumerable<string> errors) { }
        public void Complete(string id) { }
    }
}
