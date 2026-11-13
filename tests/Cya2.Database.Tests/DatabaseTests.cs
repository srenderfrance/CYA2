using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Repositories;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Abstractions;
using Cya2.Core.Entities;
using Cya2.Application.Interfaces;
using static Cya2.Database.Tests.MySqlFixture;

namespace Cya2.Database.Tests;

public sealed class DatabaseTests(MySqlFixture mysql, ITestOutputHelper output) : IClassFixture<MySqlFixture>
{
    private static readonly DateTime Date = new(2026, 1, 15);
    private static readonly AvailableDatabase Guard = new();

    [MySqlFact]
    public async Task SoftRecipientIdLinksDirectAcrossFundsAndSurvivesRelinkAndRollback()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = Donations(db);
        CanonicalDonationWriteModel Gift(bool soft, DateTime date, string id, string fund) => new()
        {
            Donor = new DonorWriteModel { Fund = fund, DisplayName = soft ? "Soft Person" : "Direct Person", IdentityKey = "initial", ResolutionSource = soft ? "SoftCredit" : "AccountName", ResolutionVersion = 1 },
            Date = date, Fund = fund, Amount = 10, PaymentMethod = "Check", GiftType = "Donation",
            AccountName = soft ? "Employer" : "Direct Person", PrimaryAddressee = soft ? "Employer" : "Direct Person",
            SoftCreditName = soft ? "Soft Person" : null, ConstituentId = soft ? "employer-id" : id,
            SoftCreditRecipientId = soft ? id : null, IsSoftCredit = soft,
            Contacts = soft ? [Email("employer@example.invalid")] : [Email("donor@example.invalid")]
        };
        await repository.ReplaceCanonicalDonationsAsync([Gift(true, Date.AddMonths(-2), "000123", "Separate Fund")], default);
        await repository.ReplaceCanonicalDonationsAsync([Gift(false, Date, "000123", "Primary Fund")], default);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT Fund) FROM DonationData"));
        Assert.Equal("000123", await ScalarAsync(db.RuntimeConnection, "SELECT SoftCreditRecipientId FROM DonationData WHERE IsSoftCredit=1"));
        Assert.Equal("employer-id", await ScalarAsync(db.RuntimeConnection, "SELECT ConstituentId FROM DonationData WHERE IsSoftCredit=1"));
        Assert.Equal("donor@example.invalid", await ScalarAsync(db.RuntimeConnection, "SELECT ContactValue FROM DonorContacts"));
        var rollback = await Rollback(db).RollbackDonationsAsync();
        Assert.True(rollback.Success, rollback.ErrorMessage);
        Assert.Equal("000123", await ScalarAsync(db.RuntimeConnection, "SELECT SoftCreditRecipientId FROM DonationData"));
        Assert.Equal("000123", await ScalarAsync(db.RuntimeConnection, "SELECT ConstituentId FROM Donors"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonorContacts"));
    }

    [MySqlFact]
    public async Task DifferentSoftRecipientIdsRemainDistinctAndBlankUsesNameFallback()
    {
        var db = await mysql.CreateDatabaseAsync();
        CanonicalDonationWriteModel Gift(string? softId, string name, bool soft) => new()
        {
            Donor = new DonorWriteModel { Fund = "Test Fund", DisplayName = name, IdentityKey = "initial", ResolutionSource = soft ? "SoftCredit" : "AccountName", ResolutionVersion = 1 },
            Date = Date, Fund = "Test Fund", Amount = 10, PaymentMethod = "Check", GiftType = "Donation",
            AccountName = soft ? "Employer" : name, PrimaryAddressee = soft ? "Employer" : name,
            SoftCreditName = soft ? name : null, ConstituentId = soft ? "employer-id" : "direct-id",
            SoftCreditRecipientId = softId, IsSoftCredit = soft
        };
        await Donations(db).ReplaceCanonicalDonationsAsync([Gift(null, "Matched", false), Gift(null, "Matched", true), Gift("other-id", "Different", true)], default);
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT DonorId) FROM DonationData"));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationData d JOIN Donors p ON d.DonorId=p.Id WHERE p.ConstituentId='direct-id'"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Donors WHERE ConstituentId='other-id'"));
    }

    [MySqlFact]
    public async Task RequiredAuditFailureRollsBackImportsAndRollbackPublication()
    {
        var db = await mysql.CreateDatabaseAsync();
        var donations = Donations(db);
        var accounting = new AccountingImportRepository(Config(db), NullLogger<AccountingImportRepository>.Instance, new NoProgress(), Guard);
        await donations.ReplaceCanonicalDonationsAsync([Donation(amount: 10)], default);
        await donations.ReplaceCanonicalDonationsAsync([Donation(amount: 20)], default);
        await accounting.ReplaceAsync([Accounting(Date, 10)], default);
        await accounting.ReplaceAsync([Accounting(Date, 20)], default);
        await ExecuteAsync(db.SetupConnection, "CREATE TRIGGER FailAudit BEFORE INSERT ON AuditEvents FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected audit failure'");

        await Assert.ThrowsAsync<AuditPersistenceException>(() => donations.ReplaceCanonicalDonationsAsync([Donation(amount: 99)], default));
        await Assert.ThrowsAsync<AuditPersistenceException>(() => accounting.ReplaceAsync([Accounting(Date, 99)], default));
        Assert.False((await Rollback(db).RollbackDonationsAsync()).Success);
        Assert.False((await Rollback(db).RollbackAccountingAsync()).Success);

        Assert.Equal("20.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM DonationData"));
        Assert.Equal("20.00", await ScalarAsync(db.RuntimeConnection, "SELECT CAST(Amount AS CHAR) FROM AccountingData"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM DonationBackupSnapshots WHERE Pinned=1"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountingBackupSnapshots WHERE Pinned=1"));
    }

    [MySqlFact]
    public async Task RequiredAuditFailureRollsBackAdministrationAndBootstrap()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "CREATE TRIGGER FailAudit BEFORE INSERT ON AuditEvents FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected audit failure'");
        var users = new UserRepository(Config(db), Guard, NullLogger<UserRepository>.Instance);
        await Assert.ThrowsAsync<AuditPersistenceException>(() => users.TryCreateInitialAdminAsync(new User { Email = "first@scene8.net", Name = "First", AuthLevel = "Admin", Language = "en-US" }));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users"));
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'user@scene8.net','Original','User')");
        var store = new UserAdministrationStore(Config(db), Guard);
        await Assert.ThrowsAsync<AuditPersistenceException>(() => store.CreateAsync(new(1, "New", "new@scene8.net", "User", [], false)));
        await Assert.ThrowsAsync<AuditPersistenceException>(() => store.UpdateAsync(new(1, 2, "Changed", null, null, null, null, false)));
        await Assert.ThrowsAsync<AuditPersistenceException>(() => store.DeleteAsync(1, 2));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users"));
        Assert.Equal("Original", await ScalarAsync(db.RuntimeConnection, "SELECT Name FROM Users WHERE Id=2"));
    }

    [MySqlFact]
    public async Task TransactionalSuccessSharesStartIdentityAndNumericReferencesSurviveDeletion()
    {
        var db = await mysql.CreateDatabaseAsync();
        var audit = new AuditEventRepository(Config(db), Guard);
        using (var operation = AuditOperationScope.Begin(1))
        {
            await audit.AddAsync(AuditOperationScope.CreateEvent(1, AuditEventTypes.DonationImportStarted, AuditOutcomes.Started));
            await Donations(db).ReplaceCanonicalDonationsAsync([Donation()], default);
            Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, $"SELECT COUNT(*) FROM AuditEvents WHERE OperationId='{operation.OperationId}' AND ActorUserId=1"));
        }
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'one@scene8.net','One','Admin'),(2,'two@scene8.net','Two','Admin')");
        var store = new UserAdministrationStore(Config(db), Guard);
        Assert.Equal(UserAdministrationStatus.Success, (await store.DeleteAsync(1, 2)).Status);
        await ExecuteAsync(db.SetupConnection, "DELETE FROM Users WHERE Id=1");
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AuditEvents WHERE EventType='AdminUserDeleted' AND ActorUserId=1 AND TargetUserId=2"));
    }

    [MySqlFact]
    public async Task RestartReconciliationIsIdempotentAndDoesNotFlagFinishedCurrentOrRecentWork()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = new AuditEventRepository(Config(db), Guard);
        var currentHost = AuditOperationScope.HostInstanceId;
        var interrupted = Guid.NewGuid().ToString("N");
        foreach (var kind in new[] { "interrupted", "complete", "current", "recent" })
        {
            var id = kind == "interrupted" ? interrupted : Guid.NewGuid().ToString("N");
            var start = new AuditEvent { OperationId = id, HostInstanceId = kind == "current" ? currentHost : Guid.NewGuid().ToString("N"), EventType = AuditEventTypes.DonationImportStarted, Outcome = AuditOutcomes.Started, OccurredAtUtc = kind == "recent" ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-1) };
            await repository.AddAsync(start);
            await repository.AddAsync(start);
            if (kind == "complete")
                await repository.AddAsync(new AuditEvent { OperationId = id, EventType = AuditEventTypes.DonationImportCompleted, Outcome = AuditOutcomes.Succeeded, OccurredAtUtc = DateTime.UtcNow });
        }
        Assert.Equal(1, await repository.FlagInterruptedImportsAsync(currentHost, DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(0, await repository.FlagInterruptedImportsAsync(currentHost, DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(interrupted, await ScalarAsync(db.RuntimeConnection, "SELECT OperationId FROM AuditEvents WHERE Outcome='NeedsReconciliation'"));
        Assert.Equal("6", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AuditEvents"));
    }

    [MySqlFact]
    public async Task AuditRetentionDeletesOnlyRecordsOlderThanCutoff()
    {
        var db = await mysql.CreateDatabaseAsync();
        var repository = new AuditEventRepository(Config(db), Guard);
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var time in new[] { cutoff.AddSeconds(-1), cutoff, cutoff.AddSeconds(1) })
            await repository.AddAsync(new AuditEvent { EventType = AuditEventTypes.DonorExportCompleted, Outcome = AuditOutcomes.Succeeded, OccurredAtUtc = time });
        Assert.Equal(1, await repository.DeleteExpiredAsync(cutoff));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AuditEvents"));
    }

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
        Assert.Equal("6", await ScalarAsync(db.SetupConnection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('DonationData', 'DonationDataBackup') AND column_name IN ('ConstituentId', 'SoftCreditRecipientId', 'IsSoftCredit')"));
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

    [MySqlFact]
    public async Task UserBatch_AuditFailureRollsBackWholeBatch()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'); CREATE TRIGGER FailAudit BEFORE INSERT ON AuditEvents FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected audit failure'");
        var store = new UserAdministrationStore(Config(db), Guard);
        await Assert.ThrowsAsync<AuditPersistenceException>(() => store.CreateBatchAsync(1, [new("First User", "first.user@scene8.net", "User"), new("Second User", "second.user@scene8.net", "Admin")]));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AuditEvents"));
    }

    [MySqlFact]
    public async Task UserBatch_CreatesDistinctAuditRecordsAndNoAccountLinks()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin')");
        var store = new UserAdministrationStore(Config(db), Guard);
        var result = await store.CreateBatchAsync(1, [new("First User", "first.user@scene8.net", "User"), new("Second User", "second.user@scene8.net", "Viewer")]);
        Assert.Equal(UserAdministrationStatus.Success, result.Status);
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT OperationId) FROM AuditEvents WHERE EventType='AdminUserCreated'"));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(DISTINCT TargetUserId) FROM AuditEvents WHERE EventType='AdminUserCreated'"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers"));
    }

    [MySqlFact]
    public async Task UserBatch_DuplicateLaterRowRollsBackUsersAndAudits()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'second.user@scene8.net','Existing','User')");
        var store = new UserAdministrationStore(Config(db), Guard);
        var result = await store.CreateBatchAsync(1, [new("First User", "first.user@scene8.net", "User"), new("Second User", "second.user@scene8.net", "Viewer")]);
        Assert.Equal(UserAdministrationStatus.DuplicateEmail, result.Status);
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AuditEvents"));
        Assert.Equal(UserAdministrationStatus.NotAuthorized, (await store.CreateBatchAsync(2, [new("Third User", "third.user@scene8.net", "Admin")])).Status);
    }

    [MySqlFact]
    public async Task UserAdministration_PreventsSelfLockoutAndConcurrentAdminLoss()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'one@scene8.net','One','Admin'),(2,'two@scene8.net','Two','Admin')");
        var store = new UserAdministrationStore(Config(db), Guard);

        Assert.Equal(UserAdministrationStatus.SelfChange, (await store.DeleteAsync(1, 1)).Status);
        Assert.Equal(UserAdministrationStatus.SelfChange, (await store.UpdateAsync(new(1, 1, null, null, "User", null, null, false))).Status);
        var results = await Task.WhenAll(store.DeleteAsync(1, 2), store.DeleteAsync(2, 1));

        Assert.Single(results, result => result.Status == UserAdministrationStatus.Success);
        Assert.Single(results, result => result.Status == UserAdministrationStatus.NotAuthorized);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users WHERE LOWER(TRIM(AuthLevel))='admin'"));
    }

    [MySqlFact]
    public async Task UserAdministration_InternCreationFailureRollsBackUserAndAccount()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'); CREATE TRIGGER FailLink BEFORE INSERT ON AccountsUsers FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected failure'");
        var store = new UserAdministrationStore(Config(db), Guard);

        await Assert.ThrowsAsync<MySqlException>(() => store.CreateAsync(new(1, "Test Intern", "intern@scene8.net", "Intern", [], false)));

        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers"));
    }

    [MySqlFact]
    public async Task UserAdministration_ConcurrentDuplicateEmailCreatesOnlyOneUser()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin')");
        var store = new UserAdministrationStore(Config(db), Guard);
        var command = new CreateUserCommand(1, "New User", "new@scene8.net", "User", [], false);

        var results = await Task.WhenAll(store.CreateAsync(command), store.CreateAsync(command));

        Assert.Single(results, result => result.Status == UserAdministrationStatus.Success);
        Assert.Single(results, result => result.Status == UserAdministrationStatus.DuplicateEmail);
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users WHERE Email='new@scene8.net'"));
    }

    [MySqlFact]
    public async Task UserAdministration_FailedDeletionPreservesUserAndAccess()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'user@scene8.net','User','User'); INSERT INTO Accounts (AccountId,Fund,AccountNumber) VALUES (1,'Fund A','1'); INSERT INTO AccountsUsers VALUES (2,1); UPDATE Users SET DefaultAccount=1 WHERE Id=2; CREATE TRIGGER FailUserDelete BEFORE DELETE ON Users FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected failure'");
        var store = new UserAdministrationStore(Config(db), Guard);

        await Assert.ThrowsAsync<MySqlException>(() => store.DeleteAsync(1, 2));

        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Users WHERE Id=2 AND DefaultAccount=1"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers WHERE UserId=2 AND AccountId=1"));
    }

    [MySqlFact]
    public async Task UserAdministration_CombinedUpdateFailureRollsBackProfileAndLinks()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'user@scene8.net','Original','User'); INSERT INTO Accounts (AccountId,Fund,AccountNumber) VALUES (1,'Fund A','1'),(2,'Fund B','2'); INSERT INTO AccountsUsers VALUES (2,1); UPDATE Users SET DefaultAccount=1 WHERE Id=2; CREATE TRIGGER FailUnlink BEFORE DELETE ON AccountsUsers FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected failure'");
        var store = new UserAdministrationStore(Config(db), Guard);

        await Assert.ThrowsAsync<MySqlException>(() => store.UpdateAsync(new(1, 2, "Changed", null, "Viewer", 2, 1, false)));

        Assert.Equal("Original", await ScalarAsync(db.RuntimeConnection, "SELECT Name FROM Users WHERE Id=2"));
        Assert.Equal("User", await ScalarAsync(db.RuntimeConnection, "SELECT AuthLevel FROM Users WHERE Id=2"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT DefaultAccount FROM Users WHERE Id=2"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers WHERE UserId=2 AND AccountId=1"));
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers WHERE UserId=2 AND AccountId=2"));
    }

    [MySqlFact]
    public async Task UserAdministration_ChangesLinksAndDefaultAtomicallyAndPreservesAccountsOnDelete()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'user@scene8.net','User','User'); INSERT INTO Accounts (AccountId,Fund,AccountNumber) VALUES (1,'Fund A','1'),(2,'Fund B','2'); INSERT INTO AccountsUsers VALUES (2,1); UPDATE Users SET DefaultAccount=1 WHERE Id=2");
        var store = new UserAdministrationStore(Config(db), Guard);

        Assert.Equal(UserAdministrationStatus.Success, (await store.UpdateAsync(new(1, 2, "Changed", null, null, 2, 1, false))).Status);
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT DefaultAccount FROM Users WHERE Id=2"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers WHERE UserId=2 AND AccountId=2"));
        Assert.Equal(UserAdministrationStatus.Success, (await store.DeleteAsync(1, 2)).Status);
        Assert.Equal("0", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM AccountsUsers WHERE UserId=2"));
        Assert.Equal("2", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts"));
    }

    [MySqlFact]
    public async Task UserAdministration_InternTypoAndExplicitReusePreserveAccount()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin')");
        var store = new UserAdministrationStore(Config(db), Guard);

        Assert.Equal(UserAdministrationStatus.Success, (await store.CreateAsync(new(1, "Test Intern", "intern@scene8.net", "Intern", [], false))).Status);
        var id = int.Parse(await ScalarAsync(db.RuntimeConnection, "SELECT Id FROM Users WHERE Email='intern@scene8.net'"));
        var accountId = await ScalarAsync(db.RuntimeConnection, $"SELECT DefaultAccount FROM Users WHERE Id={id}");
        Assert.Equal(UserAdministrationStatus.InternRoleChange, (await store.UpdateAsync(new(1, id, "Changed", null, "Admin", null, null, false))).Status);
        Assert.Equal("Test Intern", await ScalarAsync(db.RuntimeConnection, $"SELECT Name FROM Users WHERE Id={id}"));
        Assert.Equal(UserAdministrationStatus.Success, (await store.UpdateAsync(new(1, id, "Test Intrn", null, null, null, null, false))).Status);
        Assert.Equal(accountId, await ScalarAsync(db.RuntimeConnection, $"SELECT DefaultAccount FROM Users WHERE Id={id}"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts WHERE Fund='Intern: Test Intrn' AND AccountingClass IS NULL"));
        Assert.Equal(UserAdministrationStatus.Success, (await store.DeleteAsync(1, id)).Status);
        Assert.Equal(UserAdministrationStatus.InternReuseRequired, (await store.CreateAsync(new(1, "Test Intrn", "again@scene8.net", "Intern", [], false))).Status);
        Assert.Equal(UserAdministrationStatus.Success, (await store.CreateAsync(new(1, "Test Intrn", "again@scene8.net", "Intern", [], true))).Status);
        Assert.Equal(accountId, await ScalarAsync(db.RuntimeConnection, "SELECT DefaultAccount FROM Users WHERE Email='again@scene8.net'"));
        Assert.Equal("1", await ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts"));
    }

    [MySqlFact]
    public async Task UserRepository_LanguageAndIdentityBindingDoNotOverwriteAdministration()
    {
        var db = await mysql.CreateDatabaseAsync();
        await ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id,Email,Name,AuthLevel) VALUES (1,'admin@scene8.net','Admin','Admin'),(2,'user@scene8.net','Original','Admin')");
        var users = new UserRepository(Config(db), Guard, NullLogger<UserRepository>.Instance);
        var store = new UserAdministrationStore(Config(db), Guard);
        var staleUser = await users.GetByIdAsync(2);
        Assert.Equal(UserAdministrationStatus.Success, (await store.UpdateAsync(new(1, 2, "Changed", null, "User", null, null, false))).Status);

        Assert.True(await users.UpdateLanguageAsync(staleUser!.Id, "es-US"));
        var bound = await users.TryBindGoogleIdAsync(staleUser.Id, staleUser.Email, "google-test");
        Assert.NotNull(bound);
        Assert.Equal("User", bound!.AuthLevel);
        Assert.Equal("Changed", bound.Name);
        Assert.Equal("es-US", bound.Language);
        Assert.Null(await users.TryBindGoogleIdAsync(staleUser.Id, staleUser.Email, "other-google"));
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
