using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Repositories;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Abstractions;
using static Cya2.Database.Tests.MySqlFixture;

namespace Cya2.Database.Tests;

public sealed class DatabaseTests(MySqlFixture mysql, ITestOutputHelper output) : IClassFixture<MySqlFixture>
{
    private static readonly DateTime Date = new(2026, 1, 15);
    private static readonly AvailableDatabase Guard = new();

    [MySqlFact]
    public async Task InternDonationReads_MatchExactStoredNamesAndAgreeWithBulk()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Donors (DisplayName,IdentityKey,ResolutionSource,ResolutionVersion) VALUES ('Test','test','AccountName',1); INSERT INTO DonationData (DonorId,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,InternName) VALUES (1,'2026-01-15','Donor','Check','Gift',1,'Shared','Ann Lee'),(1,'2026-01-15','Donor','Check','Gift',2,'Shared','Annette Leeds'),(1,'2026-01-15','Donor','Check','Gift',3,'Shared','Lee, Ann'),(1,'2026-01-15','Donor','Check','Gift',4,'Shared','ann Lee'),(1,'2026-01-15','Donor','Check','Gift',5,'Shared','Ánn Lee'),(1,'2026-01-15','Donor','Check','Gift',6,'Shared','Ann Lee '),(1,'2026-01-15','Donor','Check','Gift',7,'Other','Ann Lee')");
        var reader = new DonationReadRepository(Config(db), Guard, NullLogger<DonationReadRepository>.Instance);
        var names = new[] { "Ann Lee", "Annette Leeds", "Lee, Ann", "ann Lee", "Ánn Lee", "Ann Lee " };

        var allNames = await reader.GetCurrentInternNamesAsync();
        var bulk = await reader.GetInternDonationsByNamesAndDateRangeAsync(names, Date.AddDays(-1), Date.AddDays(1));

        Assert.All(names, name => Assert.Contains(name, allNames));
        foreach (var name in names)
        {
            var single = await reader.GetInternDonationsByInternNameAndDateRangeAsync(name, Date.AddDays(-1), Date.AddDays(1));
            Assert.Equal(single.Select(row => row.Id).Order(), bulk[name].Select(row => row.Id).Order());
            Assert.All(single, row => Assert.Equal(name, row.InternName));
        }
        Assert.Equal(2, bulk["Ann Lee"].Count);
        Assert.All(names.Skip(1), name => Assert.Single(bulk[name]));
    }

    [MySqlFact]
    public async Task NameOnlyPromotion_PreservesDirectContactTimestamps_AndRollbackRestoresOriginalIdentity()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        CanonicalDonationWriteModel Gift(string? id, bool soft, DateTime date) => new()
        {
            Donor = new DonorWriteModel { Fund = "Test Fund", DisplayName = "Alex Smith", IdentityKey = "alexsmith", ResolutionSource = soft ? "SoftCredit" : "AccountName", ResolutionVersion = 1 },
            ConstituentId = id, IsSoftCredit = soft, AccountName = soft ? "Employer" : "Alex Smith",
            PrimaryAddressee = soft ? "Employer" : "Alex Smith", SoftCreditName = soft ? "Alex Smith" : null,
            Date = date, Fund = "Test Fund", Amount = 10, PaymentMethod = "Check", GiftType = "Donation",
            Contacts = soft ? [] : [Email("alex@example.invalid")]
        };
        await repository.ReplaceCanonicalDonationsAsync([Gift(null, false, Date.AddMonths(-2))], default);
        await ExecuteAsync(db.SetupConnection, "UPDATE DonorContacts SET DateCreated='2020-01-01', DateModified='2021-01-01'");
        var original = await ScalarAsync(db.RuntimeConnection, "SELECT DonorId FROM DonationData");
        await repository.ReplaceCanonicalDonationsAsync([Gift("000123", false, Date)], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE DateCreated='2020-01-01' AND DateModified='2021-01-01'"));
        var rollback = await Rollback(db).RollbackDonationsAsync();
        Assert.True(rollback.Success, rollback.ErrorMessage);
        Assert.Equal(original, await ScalarAsync(db.RuntimeConnection, "SELECT DonorId FROM DonationData"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE DateCreated='2020-01-01' AND DateModified='2021-01-01'"));
    }

    [MySqlFact]
    public async Task NewAmbiguity_SeparatesRetainedSoftCreditsWithoutCopyingContacts_AndRollbackRelinks()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        CanonicalDonationWriteModel Gift(string id, bool soft, DateTime date) => new()
        {
            Donor = new DonorWriteModel { Fund = "Test Fund", DisplayName = "Same Name", IdentityKey = "samename", ResolutionSource = soft ? "SoftCredit" : "AccountName", ResolutionVersion = 1 },
            ConstituentId = id, IsSoftCredit = soft, AccountName = soft ? "Employer" : "Same Name",
            PrimaryAddressee = soft ? "Employer" : "Same Name", SoftCreditName = soft ? "Same Name" : null,
            Date = date, Fund = "Test Fund", Amount = 10, PaymentMethod = "Check", GiftType = "Donation",
            Contacts = soft ? [] : [Email(id + "@example.invalid")]
        };
        await repository.ReplaceCanonicalDonationsAsync([
            Gift("1", false, Date.AddMonths(-2)), Gift("employer", true, Date.AddMonths(-1))], default);
        await repository.ReplaceCanonicalDonationsAsync([Gift("2", false, Date)], default);
        Assert.Equal("3", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts c JOIN Donors d ON d.Id=c.DonorId WHERE d.ConstituentId IS NULL"));
        Assert.Equal("employer", await ScalarAsync(db.RuntimeConnection, "SELECT ConstituentId FROM DonationData WHERE IsSoftCredit=1"));
        var rollback = await Rollback(db).RollbackDonationsAsync();
        Assert.True(rollback.Success, rollback.ErrorMessage);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        Assert.Equal("1@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
    }

    [MySqlFact]
    public async Task SharedConstituentIdentity_CombinesFunds_IgnoresOrganizationIds_AndRollsBackRelinks()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        CanonicalDonationWriteModel Gift(string name, string? id, string fund, bool soft = false, DateTime? date = null, string? email = null) => new()
        {
            Donor = new DonorWriteModel { Fund = fund, DisplayName = name, IdentityKey = name, ResolutionSource = soft ? "SoftCredit" : "AccountName", ResolutionVersion = 1 },
            Date = date ?? Date, AccountName = soft ? "Employer" : name, PrimaryAddressee = soft ? "Employer" : name,
            SoftCreditName = soft ? name : null, ConstituentId = id, IsSoftCredit = soft,
            Fund = fund, Amount = 10, PaymentMethod = "Check", GiftType = "Donation", Contacts = email == null ? [] : [Email(email)]
        };
        await repository.ReplaceCanonicalDonationsAsync([Gift("Alex Smith", "employer", "A", true, Date.AddMonths(-1))], default);
        var originalId = await ScalarAsync(db.RuntimeConnection, "SELECT DonorId FROM DonationData");
        await repository.ReplaceCanonicalDonationsAsync([
            Gift("Alex Smith", "000123", "B", email: "alex@example.invalid"),
            Gift("Smith, Alex", "employer", "C", true, email: "employer@example.invalid"),
            Gift("Other Person", "employer", "C", true)
        ], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData WHERE Fund IN ('A','B')"));
        Assert.Equal("3", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData d JOIN Donors n ON n.Id=d.DonorId WHERE n.ConstituentId='000123'"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors WHERE ConstituentId='employer'"));
        Assert.Equal("alex@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
        var rollback = await Rollback(db).RollbackDonationsAsync();
        Assert.True(rollback.Success, rollback.ErrorMessage);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
        Assert.Equal(originalId, await ScalarAsync(db.RuntimeConnection, "SELECT DonorId FROM DonationData"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts"));
    }

    [MySqlFact]
    public async Task SharedIdentity_AmbiguousNamesRemainSeparate_AndSoftOnlyUploadKeepsDirectContacts()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        CanonicalDonationWriteModel Gift(string? id, bool soft, DateTime date) => new()
        {
            Donor = new DonorWriteModel { Fund = "Test Fund", DisplayName = "Same Name", IdentityKey = "samename", ResolutionSource = "AccountName", ResolutionVersion = 1 },
            ConstituentId = id, IsSoftCredit = soft, AccountName = soft ? "Employer" : "Same Name", PrimaryAddressee = soft ? "Employer" : "Same Name", SoftCreditName = soft ? "Same Name" : null,
            Date = date, Fund = "Test Fund", Amount = 10, PaymentMethod = "Check", GiftType = "Donation", Contacts = soft ? [] : [Email("direct@example.invalid")]
        };
        await repository.ReplaceCanonicalDonationsAsync([Gift("1", false, Date.AddMonths(-1)), Gift("employer", true, Date)], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        await repository.ReplaceCanonicalDonationsAsync([Gift("employer", true, Date)], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts"));
        await repository.ReplaceCanonicalDonationsAsync([Gift("2", false, Date), Gift("employer", true, Date)], default);
        Assert.Equal("3", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
    }

    [MySqlFact]
    public async Task FreshSchema_InitializesWithoutHistory_AndRejectsRerunsWithoutChangingData()
    {
        var db = await mysql.CreateDatabaseAsync();
        Assert.Equal("0", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='Cya2SchemaMigrations'"));
        Assert.Equal("2", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('DonationBackupSnapshots', 'AccountingBackupSnapshots') AND column_name='BackupAt' AND datetime_precision=6"));
        Assert.Equal("4", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('DonationData', 'DonationDataBackup') AND column_name IN ('ConstituentId', 'IsSoftCredit')"));
        Assert.Equal("1", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='AuditEvents'"));
        await Donations(db).ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("original@example.invalid")])], default);

        var rerun = await MigrateAsync(db.SetupConnection);
        Assert.True(rerun.ExitCode == 1, rerun.Output);
        Assert.Contains("requires --initialize", rerun.Output, StringComparison.OrdinalIgnoreCase);

        var reinitialize = await MigrateAsync(db.SetupConnection, initialize: true);
        Assert.True(reinitialize.ExitCode == 1, reinitialize.Output);
        Assert.Contains("requires an empty database", reinitialize.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData"));
        Assert.Equal("10.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM DonationData"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors"));
        Assert.Equal("original@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationBackupSnapshots"));
        Assert.Equal("0", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='Cya2SchemaMigrations'"));
    }

    [MySqlFact]
    public async Task ContactFreshness_PreservesUnchangedTimestamps_TracksExactChanges_AndRestoresOnRollback()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("keep@example.invalid"), Email("old@example.invalid")])], default);
        await ExecuteAsync(db.SetupConnection, "UPDATE DonorContacts SET DateCreated='2020-01-01 00:00:00', DateModified='2021-01-01 00:00:00'");
        var created = await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE ContactValue='keep@example.invalid'");
        var modified = await ScalarAsync(db.RuntimeConnection, "SELECT DateModified FROM DonorContacts WHERE ContactValue='keep@example.invalid'");
        var id = await ScalarAsync(db.RuntimeConnection, "SELECT Id FROM DonorContacts WHERE ContactValue='keep@example.invalid'");

        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("keep@example.invalid"), Email("old@example.invalid")])], default);
        Assert.Equal(created, await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE ContactValue='keep@example.invalid'"));
        Assert.Equal(modified, await ScalarAsync(db.RuntimeConnection, "SELECT DateModified FROM DonorContacts WHERE ContactValue='keep@example.invalid'"));

        var changed = new DonorContactWriteModel { ContactType = "Email", ContactValue = "KEEP@example.invalid", NormalizedValue = "keep@example.invalid" };
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [changed, Email("new@example.invalid")])], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE BINARY ContactValue='KEEP@example.invalid' AND DateModified > '2021-01-01 00:00:00'"));
        Assert.Equal(id, await ScalarAsync(db.RuntimeConnection, "SELECT Id FROM DonorContacts WHERE NormalizedValue='keep@example.invalid'"));
        Assert.Equal(created, await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE NormalizedValue='keep@example.invalid'"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='new@example.invalid' AND DateCreated IS NOT NULL AND DateModified IS NULL"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='old@example.invalid'"));

        var changedAt = await ScalarAsync(db.RuntimeConnection, "SELECT DateModified FROM DonorContacts WHERE NormalizedValue='keep@example.invalid'");
        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [changed, Email("new@example.invalid")])], default);
        Assert.Equal(changedAt, await ScalarAsync(db.RuntimeConnection, "SELECT DateModified FROM DonorContacts WHERE NormalizedValue='keep@example.invalid'"));

        await repository.ReplaceCanonicalDonationsAsync([Donation(contacts: [Email("keep@example.invalid"), Email("old@example.invalid")])], default);
        var result = await Rollback(db).RollbackDonationsAsync();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(changedAt, await ScalarAsync(db.RuntimeConnection, "SELECT DateModified FROM DonorContacts WHERE BINARY ContactValue='KEEP@example.invalid'"));
        Assert.Equal(created, await ScalarAsync(db.RuntimeConnection, "SELECT DateCreated FROM DonorContacts WHERE NormalizedValue='keep@example.invalid'"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='new@example.invalid' AND DateModified IS NULL"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='old@example.invalid'"));
    }

    [MySqlFact]
    public async Task ContactFreshness_EmptyContactSetClearsImportedDonorOnly()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        await repository.ReplaceCanonicalDonationsAsync([
            Donation(contacts: [Email("included@example.invalid")]),
            Donation(identity: "unrelated", date: Date.AddMonths(-1), contacts: [Email("untouched@example.invalid")])
        ], default);
        await repository.ReplaceCanonicalDonationsAsync([Donation()], default);
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='included@example.invalid'"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='untouched@example.invalid' AND DateModified IS NULL"));
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
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts WHERE ContactValue='keep@example.invalid' AND DateModified IS NULL"));
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
    private RollbackExecutor Rollback(TestDatabase db) => new(Config(db), new TestOutputLogger<RollbackExecutor>(output),
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
