using Cya2.Core.DTOs;
using Cya2.Core.Enums;
using Cya2.Core.Interfaces;
using Cya2.Core.Services;
using Cya2.Core.ReadModels;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class DonationRepository : IDonationRepository
{
    private static readonly string[] ContactTypes =
    [
        "Email", "HomePhone", "MobilePhone", "AddressLine1",
        "City", "State", "PostalCode", "Country"
    ];
    private const int ImportCommandTimeoutSeconds = 60;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;
    private readonly ILogger<DonationRepository> _logger;

    public DonationRepository(
        IConfiguration configuration,
        IDatabaseGuard dbGuard,
        ILogger<DonationRepository> logger)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
        _logger = logger;
    }

    private string ConnectionString => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task BackupAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var backupId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation("Donation backup transaction opened. ProgressId={ProgressId}, BackupId={BackupId}, SourceRangeStart={SourceRangeStart:O}", progressId, backupId, fromDate);
            await EnsureDonationBackupTablesAsync(connection, (MySqlTransaction)transaction, cancellationToken);
            _logger.LogInformation("Donation backup tables verified. ProgressId={ProgressId}, BackupId={BackupId}", progressId, backupId);

            var donationBackupRows = 0;
            await using (var backup = connection.CreateCommand())
            {
                backup.Transaction = transaction;
                backup.CommandText = @"
INSERT INTO DonationDataBackup
    (BackupId, Id, DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType,
     Amount, Fund, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName,
     IsAnonymous, Frequency, DateCreated, BackupAt, Pinned, SourceRangeStart)
SELECT @BackupId, Id, DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType,
       Amount, Fund, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName,
       IsAnonymous, Frequency, DateCreated, UTC_TIMESTAMP(), 0, @SourceRangeStart
FROM DonationData
WHERE Date >= @SourceRangeStart";
                backup.Parameters.AddWithValue("@BackupId", backupId);
                backup.Parameters.AddWithValue("@SourceRangeStart", fromDate);
                donationBackupRows = await backup.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("DonationDataBackup populated. ProgressId={ProgressId}, BackupId={BackupId}, Rows={Rows}", progressId, backupId, donationBackupRows);
            }

            await using (var snapshot = connection.CreateCommand())
            {
                snapshot.Transaction = transaction;
                snapshot.CommandText = @"
INSERT INTO DonationBackupSnapshots
    (BackupId, BackupAt, SourceRangeStart, RecordCount, Pinned)
VALUES (@BackupId, UTC_TIMESTAMP(), @SourceRangeStart, @RecordCount, 0)";
                snapshot.Parameters.AddWithValue("@BackupId", backupId);
                snapshot.Parameters.AddWithValue("@SourceRangeStart", fromDate);
                snapshot.Parameters.AddWithValue("@RecordCount", donationBackupRows);
                await snapshot.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var donorBackup = connection.CreateCommand())
            {
                donorBackup.Transaction = transaction;
                donorBackup.CommandText = @"
INSERT INTO DonorsBackup
    (BackupId, Id, Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion,
     DateCreated, DateModified, BackupAt)
SELECT DISTINCT @BackupId, donor.Id, donor.Fund, donor.DisplayName, donor.IdentityKey,
       donor.ResolutionSource, donor.ResolutionVersion, donor.DateCreated,
       donor.DateModified, UTC_TIMESTAMP()
FROM Donors donor
INNER JOIN DonationData donation ON donation.DonorId = donor.Id
WHERE donation.Date >= @SourceRangeStart";
                donorBackup.Parameters.AddWithValue("@BackupId", backupId);
                donorBackup.Parameters.AddWithValue("@SourceRangeStart", fromDate);
                var donorBackupRows = await donorBackup.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("DonorsBackup populated. ProgressId={ProgressId}, BackupId={BackupId}, Rows={Rows}", progressId, backupId, donorBackupRows);
            }

            await using (var contactBackup = connection.CreateCommand())
            {
                contactBackup.Transaction = transaction;
                contactBackup.CommandText = @"
INSERT INTO DonorContactsBackup
    (BackupId, Id, DonorId, ContactType, ContactValue, NormalizedValue,
     DateCreated, DateModified, BackupAt)
SELECT @BackupId, contact.Id, contact.DonorId, contact.ContactType,
       contact.ContactValue, contact.NormalizedValue, contact.DateCreated,
       contact.DateModified, UTC_TIMESTAMP()
FROM DonorContacts contact
INNER JOIN DonorsBackup donor ON donor.BackupId = @BackupId
                                    AND donor.Id = contact.DonorId";
                contactBackup.Parameters.AddWithValue("@BackupId", backupId);
                var contactBackupRows = await contactBackup.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("DonorContactsBackup populated. ProgressId={ProgressId}, BackupId={BackupId}, Rows={Rows}", progressId, backupId, contactBackupRows);
            }

            await CleanupDonationBackupsAsync(connection, (MySqlTransaction)transaction, backupId, cancellationToken);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM DonationData WHERE Date >= @SourceRangeStart";
                delete.Parameters.AddWithValue("@SourceRangeStart", fromDate);
                var deletedRows = await delete.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Overlapping DonationData deleted. ProgressId={ProgressId}, BackupId={BackupId}, Rows={Rows}", progressId, backupId, deletedRows);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureDonationBackupTablesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var donorBackup = connection.CreateCommand();
        donorBackup.Transaction = transaction;
        donorBackup.CommandText = @"
CREATE TABLE IF NOT EXISTS DonorsBackup
(
    BackupId CHAR(36) NOT NULL,
    Id BIGINT NOT NULL,
    Fund VARCHAR(255) NOT NULL,
    DisplayName VARCHAR(255) NOT NULL,
    IdentityKey VARCHAR(512) NOT NULL,
    ResolutionSource VARCHAR(32) NOT NULL,
    ResolutionVersion INT NOT NULL,
    DateCreated DATETIME NOT NULL,
    DateModified DATETIME NOT NULL,
    BackupAt DATETIME NOT NULL,
    PRIMARY KEY (BackupId, Id),
    KEY ix_DonorsBackup_BackupId (BackupId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS DonorContactsBackup
(
    BackupId CHAR(36) NOT NULL,
    Id BIGINT NOT NULL,
    DonorId BIGINT NOT NULL,
    ContactType VARCHAR(32) NOT NULL,
    ContactValue VARCHAR(500) NOT NULL,
    NormalizedValue VARCHAR(500) NOT NULL,
    DateCreated DATETIME NOT NULL,
    DateModified DATETIME NOT NULL,
    BackupAt DATETIME NOT NULL,
    PRIMARY KEY (BackupId, Id),
    KEY ix_DonorContactsBackup_BackupId (BackupId),
    KEY ix_DonorContactsBackup_DonorId (BackupId, DonorId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci";
        await donorBackup.ExecuteNonQueryAsync(cancellationToken);

        await using var snapshot = connection.CreateCommand();
        snapshot.Transaction = transaction;
        snapshot.CommandText = @"
CREATE TABLE IF NOT EXISTS DonationBackupSnapshots
(
    BackupId CHAR(36) NOT NULL PRIMARY KEY,
    BackupAt DATETIME NOT NULL,
    SourceRangeStart DATETIME NOT NULL,
    RecordCount INT NOT NULL DEFAULT 0,
    Pinned BOOLEAN NOT NULL DEFAULT FALSE,
    KEY ix_DonationBackupSnapshots_BackupAt (BackupAt),
    KEY ix_DonationBackupSnapshots_Pinned (Pinned)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci";
        await snapshot.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CleanupDonationBackupsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string currentBackupId,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
        {
            "DonationDataBackup",
            "DonorContactsBackup",
            "DonorsBackup",
            "DonationBackupSnapshots"
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = ImportCommandTimeoutSeconds;
            command.CommandText = $"DELETE FROM {table} WHERE BackupId <> @BackupId";
            command.Parameters.AddWithValue("@BackupId", currentBackupId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<List<DonationRecord>> GetRecentDonationsForDonorsAsync(
        IEnumerable<(string IdentityKey, string Fund)> donorKeys,
        DateTime beforeDate,
        int maxPerDonor,
        CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();

        var keys = donorKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.IdentityKey) && !string.IsNullOrWhiteSpace(key.Fund))
            .Distinct()
            .ToList();

        if (keys.Count == 0 || maxPerDonor <= 0)
        {
            return [];
        }

        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var stage = connection.CreateCommand())
        {
            stage.CommandTimeout = ImportCommandTimeoutSeconds;
            stage.CommandText = @"
CREATE TEMPORARY TABLE TempFrequencyDonorKeys
(
    StageId BIGINT NOT NULL AUTO_INCREMENT,
    Fund VARCHAR(255) NOT NULL,
    IdentityKey VARCHAR(512) NOT NULL,
    PRIMARY KEY (StageId),
    KEY ix_TempFrequencyDonorKeys_Lookup (Fund(100), IdentityKey(200))
) ENGINE=InnoDB";
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        const int chunkSize = 500;
        for (var offset = 0; offset < keys.Count; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, keys.Count - offset);
            await using var insert = connection.CreateCommand();
            insert.CommandTimeout = ImportCommandTimeoutSeconds;
            var values = new StringBuilder();
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    values.Append(',');
                }

                values.Append($"(@Fund{index}, @IdentityKey{index})");
                insert.Parameters.AddWithValue($"@Fund{index}", keys[offset + index].Fund);
                insert.Parameters.AddWithValue($"@IdentityKey{index}", keys[offset + index].IdentityKey);
            }

            insert.CommandText = $"INSERT INTO TempFrequencyDonorKeys (Fund, IdentityKey) VALUES {values}";
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = ImportCommandTimeoutSeconds;
        command.CommandText = @"
SELECT r.Id, r.DonorId, r.AccountName, r.Fund, r.Date, r.Amount, r.PaymentMethod, r.Frequency, r.IdentityKey
FROM (
    SELECT d.Id, d.DonorId, d.AccountName, d.Fund, d.Date, d.Amount, d.PaymentMethod, d.Frequency,
           donor.IdentityKey,
           ROW_NUMBER() OVER (
               PARTITION BY d.DonorId, d.Fund
               ORDER BY d.Date DESC, d.Id DESC
           ) AS RowNumber
    FROM DonationData d
    INNER JOIN Donors donor ON donor.Id = d.DonorId
    INNER JOIN TempFrequencyDonorKeys requested
        ON requested.Fund COLLATE utf8mb4_0900_ai_ci = donor.Fund COLLATE utf8mb4_0900_ai_ci
       AND requested.IdentityKey COLLATE utf8mb4_0900_ai_ci = donor.IdentityKey COLLATE utf8mb4_0900_ai_ci
    WHERE d.Date < @BeforeDate
) r
WHERE r.RowNumber <= @MaxPerDonor
ORDER BY r.AccountName, r.Fund, r.Date DESC";
        command.Parameters.AddWithValue("@BeforeDate", beforeDate);
        command.Parameters.AddWithValue("@MaxPerDonor", maxPerDonor);

        var results = new List<DonationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var frequencyOrdinal = reader.GetOrdinal("Frequency");
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DonationRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                DonorId = reader.GetInt64(reader.GetOrdinal("DonorId")),
                AccountName = reader.IsDBNull(reader.GetOrdinal("AccountName")) ? string.Empty : reader.GetString(reader.GetOrdinal("AccountName")),
                Fund = reader.GetString(reader.GetOrdinal("Fund")),
                Date = reader.GetDateTime(reader.GetOrdinal("Date")),
                Amount = reader.GetDouble(reader.GetOrdinal("Amount")),
                PaymentMethod = reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? string.Empty : reader.GetString(reader.GetOrdinal("PaymentMethod")),
                Frequency = reader.IsDBNull(frequencyOrdinal) ? null : (DonorFrequency)reader.GetInt32(frequencyOrdinal)
            });
        }

        return results;
    }

    public async Task<int> RecategorizeAllDonationsAsync(CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var load = connection.CreateCommand();
            load.Transaction = transaction;
            load.CommandText = @"
SELECT Id, DonorId, Fund, Date, Amount
FROM DonationData
ORDER BY DonorId, Fund, Date, Id";
            var records = new List<(int Id, long DonorId, string Fund, DateTime Date, decimal Amount)>();
            await using (var reader = await load.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    records.Add((
                        reader.GetInt32(0),
                        reader.GetInt64(1),
                        reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        reader.GetDateTime(3),
                        reader.GetDecimal(4)));
                }
            }

            var frequencyService = new DonorFrequencyService();
            var updates = records
                .GroupBy(record => new
                {
                    DonorId = record.DonorId,
                    Fund = string.IsNullOrWhiteSpace(record.Fund) ? "__NO_FUND__" : record.Fund.Trim().ToUpperInvariant()
                })
                .SelectMany(group =>
                {
                    var sorted = group.OrderBy(record => record.Date).ThenBy(record => record.Id).ToList();
                    var history = sorted.Select(record => new DonorGiftRecord { Date = record.Date, Amount = (decimal)record.Amount }).ToList();
                    return sorted.Select((record, index) =>
                    {
                        var classification = frequencyService.ClassifyGift(history[index], history).Frequency;
                        return (record.Id, Frequency: classification == DonorFrequency.None ? DonorFrequency.OneTime : classification);
                    });
                })
                .ToList();

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE DonationData SET Frequency = @Frequency WHERE Id = @Id";
            update.Parameters.Add("@Frequency", MySqlDbType.Int32);
            update.Parameters.Add("@Id", MySqlDbType.Int32);
            var updated = 0;
            foreach (var item in updates)
            {
                update.Parameters["@Frequency"].Value = (int)item.Frequency;
                update.Parameters["@Id"].Value = item.Id;
                updated += await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DonorRecord?> FindDonorAsync(string fund, string identityKey, CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
 SELECT Id, Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion
FROM Donors
WHERE Fund = @Fund AND IdentityKey = @IdentityKey";
        command.Parameters.AddWithValue("@Fund", fund);
        command.Parameters.AddWithValue("@IdentityKey", identityKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDonor(reader);
    }

    public async Task<DonorRecord> CreateDonorAsync(DonorWriteModel donor, CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT INTO Donors
    (Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion)
VALUES
    (@Fund, @DisplayName, @IdentityKey, @ResolutionSource, @ResolutionVersion)
AS incoming
ON DUPLICATE KEY UPDATE
    DisplayName = incoming.DisplayName,
    ResolutionSource = incoming.ResolutionSource,
    ResolutionVersion = incoming.ResolutionVersion,
    DateModified = CURRENT_TIMESTAMP";
                AddDonorParameters(insert, donor);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = @"
 SELECT Id, Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion
FROM Donors
WHERE Fund = @Fund AND IdentityKey = @IdentityKey";
            select.Parameters.AddWithValue("@Fund", donor.Fund);
            select.Parameters.AddWithValue("@IdentityKey", donor.IdentityKey);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The donor was not available after the upsert completed.");
            }

            var result = ReadDonor(reader);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DonationImportPersistenceResult> InsertCanonicalDonationsAsync(
        IReadOnlyList<CanonicalDonationWriteModel> donations,
        CancellationToken cancellationToken)
    {
        _dbGuard.ThrowIfUnavailable();
        if (donations.Count == 0)
        {
            return new DonationImportPersistenceResult();
        }

        _logger.LogInformation("Canonical donation set-based batch persistence starting. BatchCount={BatchCount}", donations.Count);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _logger.LogInformation("Canonical donation set-based transaction opened. BatchCount={BatchCount}", donations.Count);

        try
        {
            await CreateCanonicalImportStageAsync(connection, transaction, cancellationToken);
            _logger.LogInformation("Canonical donation staging tables created. BatchCount={BatchCount}", donations.Count);
            await InsertDonorStageAsync(connection, transaction, donations, cancellationToken);
            _logger.LogInformation("Canonical donor stage populated. BatchCount={BatchCount}", donations.Count);
            await InsertDonationStageAsync(connection, transaction, donations, cancellationToken);
            _logger.LogInformation("Canonical donation stage populated. BatchCount={BatchCount}", donations.Count);
            await InsertContactStageAsync(connection, transaction, donations, cancellationToken);
            _logger.LogInformation("Canonical contact stage populated. BatchCount={BatchCount}", donations.Count);
            await UpsertStagedDonorsAsync(connection, transaction, cancellationToken);
            _logger.LogInformation("Canonical staged donors upserted. BatchCount={BatchCount}", donations.Count);

            var inserted = await InsertStagedDonationsAsync(connection, transaction, cancellationToken);
            _logger.LogInformation("Canonical staged donations inserted. BatchCount={BatchCount}, Inserted={Inserted}", donations.Count, inserted);
            var contactsAdded = await ReconcileStagedContactsAsync(connection, transaction, cancellationToken);

            _logger.LogInformation("Canonical donation set-based rows and contacts prepared. BatchCount={BatchCount}, Inserted={Inserted}, ContactsAdded={ContactsAdded}", donations.Count, inserted, contactsAdded);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Canonical donation set-based transaction committed. BatchCount={BatchCount}, Inserted={Inserted}, ContactsAdded={ContactsAdded}", donations.Count, inserted, contactsAdded);
            return new DonationImportPersistenceResult
            {
                Inserted = inserted,
                ContactsAdded = contactsAdded
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task CreateCanonicalImportStageAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteImportCommandAsync(connection, transaction, @"
DROP TEMPORARY TABLE IF EXISTS TempImportDonors;
DROP TEMPORARY TABLE IF EXISTS TempImportDonations;
DROP TEMPORARY TABLE IF EXISTS TempImportContacts;
CREATE TEMPORARY TABLE TempImportDonors
(
    StageId BIGINT NOT NULL AUTO_INCREMENT,
    Fund VARCHAR(255) NOT NULL,
    IdentityKey VARCHAR(512) NOT NULL,
    DisplayName VARCHAR(255) NOT NULL,
    ResolutionSource VARCHAR(32) NOT NULL,
    ResolutionVersion INT NOT NULL,
    PRIMARY KEY (StageId),
    KEY ix_TempImportDonors_Lookup (Fund(100), IdentityKey(200))
) ENGINE=InnoDB;
CREATE TEMPORARY TABLE TempImportDonations
(
    StageId BIGINT NOT NULL AUTO_INCREMENT,
    Fund VARCHAR(255) NOT NULL,
    IdentityKey VARCHAR(512) NOT NULL,
    Date DATETIME NOT NULL,
    GiftImportId VARCHAR(255) NULL,
    AccountName VARCHAR(255) NOT NULL,
    PaymentMethod VARCHAR(255) NOT NULL,
    GiftType VARCHAR(255) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Intern VARCHAR(255) NULL,
    PrimaryAddressee VARCHAR(255) NULL,
    SoftCreditName VARCHAR(255) NULL,
    HonorMemorialName VARCHAR(255) NULL,
    IsAnonymous BOOLEAN NOT NULL,
    Frequency TINYINT NULL,
    PRIMARY KEY (StageId),
    KEY ix_TempImportDonations_Lookup (Fund(100), IdentityKey(200))
) ENGINE=InnoDB;
CREATE TEMPORARY TABLE TempImportContacts
(
    StageId BIGINT NOT NULL AUTO_INCREMENT,
    Fund VARCHAR(255) NOT NULL,
    IdentityKey VARCHAR(512) NOT NULL,
    ContactType VARCHAR(32) NOT NULL,
    ContactValue VARCHAR(500) NOT NULL,
    NormalizedValue VARCHAR(500) NOT NULL,
    PRIMARY KEY (StageId),
    KEY ix_TempImportContacts_Lookup (Fund(100), IdentityKey(200)),
    KEY ix_TempImportContacts_Value (ContactType, NormalizedValue(200))
) ENGINE=InnoDB;
TRUNCATE TABLE TempImportDonors;
TRUNCATE TABLE TempImportDonations;
TRUNCATE TABLE TempImportContacts;", cancellationToken);
    }

    private static async Task InsertDonorStageAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<CanonicalDonationWriteModel> donations,
        CancellationToken cancellationToken)
    {
        var donors = donations
            .Select(donation => donation.Donor)
            .GroupBy(donor => (donor.Fund, donor.IdentityKey), StringTupleComparer.Instance)
            .Select(group => group.Last())
            .ToList();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = ImportCommandTimeoutSeconds;
        var values = new StringBuilder();
        for (var index = 0; index < donors.Count; index++)
        {
            if (index > 0) values.Append(',');
            values.Append($"(@df{index}, @di{index}, @dn{index}, @ds{index}, @dv{index})");
            command.Parameters.AddWithValue($"@df{index}", donors[index].Fund);
            command.Parameters.AddWithValue($"@di{index}", donors[index].IdentityKey);
            command.Parameters.AddWithValue($"@dn{index}", donors[index].DisplayName);
            command.Parameters.AddWithValue($"@ds{index}", donors[index].ResolutionSource);
            command.Parameters.AddWithValue($"@dv{index}", donors[index].ResolutionVersion);
        }
        command.CommandText = $"INSERT INTO TempImportDonors (Fund, IdentityKey, DisplayName, ResolutionSource, ResolutionVersion) VALUES {values}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDonationStageAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<CanonicalDonationWriteModel> donations,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 500;
        for (var offset = 0; offset < donations.Count; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, donations.Count - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = ImportCommandTimeoutSeconds;
            var values = new StringBuilder();
            for (var localIndex = 0; localIndex < count; localIndex++)
            {
                var parameterIndex = localIndex;
                if (localIndex > 0) values.Append(',');
                values.Append($"(@f{parameterIndex}, @i{parameterIndex}, @d{parameterIndex}, @g{parameterIndex}, @a{parameterIndex}, @p{parameterIndex}, @t{parameterIndex}, @m{parameterIndex}, @n{parameterIndex}, @r{parameterIndex}, @s{parameterIndex}, @h{parameterIndex}, @x{parameterIndex}, @q{parameterIndex})");
                var donation = donations[offset + localIndex];
                command.Parameters.AddWithValue($"@f{parameterIndex}", donation.Fund);
                command.Parameters.AddWithValue($"@i{parameterIndex}", donation.Donor.IdentityKey);
                command.Parameters.AddWithValue($"@d{parameterIndex}", donation.Date);
                command.Parameters.AddWithValue($"@g{parameterIndex}", (object?)donation.GiftImportId ?? DBNull.Value);
                command.Parameters.AddWithValue($"@a{parameterIndex}", donation.AccountName);
                command.Parameters.AddWithValue($"@p{parameterIndex}", donation.PaymentMethod);
                command.Parameters.AddWithValue($"@t{parameterIndex}", donation.GiftType);
                command.Parameters.AddWithValue($"@m{parameterIndex}", donation.Amount);
                command.Parameters.AddWithValue($"@n{parameterIndex}", (object?)donation.Intern ?? DBNull.Value);
                command.Parameters.AddWithValue($"@r{parameterIndex}", (object?)donation.PrimaryAddressee ?? DBNull.Value);
                command.Parameters.AddWithValue($"@s{parameterIndex}", (object?)donation.SoftCreditName ?? DBNull.Value);
                command.Parameters.AddWithValue($"@h{parameterIndex}", (object?)donation.HonorMemorialName ?? DBNull.Value);
                command.Parameters.AddWithValue($"@x{parameterIndex}", donation.IsAnonymous);
                command.Parameters.AddWithValue($"@q{parameterIndex}", donation.Frequency.HasValue ? (object)(int)donation.Frequency.Value : DBNull.Value);
            }
            command.CommandText = $"INSERT INTO TempImportDonations (Fund, IdentityKey, Date, GiftImportId, AccountName, PaymentMethod, GiftType, Amount, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName, IsAnonymous, Frequency) VALUES {values}";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertContactStageAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<CanonicalDonationWriteModel> donations,
        CancellationToken cancellationToken)
    {
        var contacts = donations
            .SelectMany(donation => donation.Contacts.Select(contact => (donation.Donor.Fund, donation.Donor.IdentityKey, Contact: contact)))
            .DistinctBy(item => (item.Fund, item.IdentityKey, item.Contact.ContactType, item.Contact.NormalizedValue))
            .ToList();
        const int chunkSize = 500;
        for (var offset = 0; offset < contacts.Count; offset += chunkSize)
        {
            var chunk = contacts.Skip(offset).Take(chunkSize).ToList();
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = ImportCommandTimeoutSeconds;
            var values = new StringBuilder();
            for (var index = 0; index < chunk.Count; index++)
            {
                if (index > 0) values.Append(',');
                values.Append($"(@cf{index}, @ci{index}, @ct{index}, @cv{index}, @cn{index})");
                command.Parameters.AddWithValue($"@cf{index}", chunk[index].Fund);
                command.Parameters.AddWithValue($"@ci{index}", chunk[index].IdentityKey);
                command.Parameters.AddWithValue($"@ct{index}", chunk[index].Contact.ContactType);
                command.Parameters.AddWithValue($"@cv{index}", chunk[index].Contact.ContactValue);
                command.Parameters.AddWithValue($"@cn{index}", chunk[index].Contact.NormalizedValue);
            }
            command.CommandText = $"INSERT INTO TempImportContacts (Fund, IdentityKey, ContactType, ContactValue, NormalizedValue) VALUES {values}";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertStagedDonorsAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteImportCommandAsync(connection, transaction, @"
INSERT INTO Donors (Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion)
SELECT Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion
FROM TempImportDonors
ON DUPLICATE KEY UPDATE
    DisplayName = VALUES(DisplayName),
    ResolutionSource = VALUES(ResolutionSource),
    ResolutionVersion = VALUES(ResolutionVersion),
    DateModified = CURRENT_TIMESTAMP;", cancellationToken);
    }

    private static async Task<int> InsertStagedDonationsAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        return await ExecuteImportCommandAsync(connection, transaction, @"
INSERT INTO DonationData
    (DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType, Amount, Fund,
     Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName, IsAnonymous, Frequency)
SELECT d.Id, s.Date, s.GiftImportId, s.AccountName, s.PaymentMethod, s.GiftType, s.Amount, s.Fund,
       s.Intern, s.PrimaryAddressee, s.SoftCreditName, s.HonorMemorialName, s.IsAnonymous, s.Frequency
FROM TempImportDonations s
JOIN Donors d ON d.Fund = s.Fund AND d.IdentityKey = s.IdentityKey;", cancellationToken);
    }

    private static async Task<int> ReconcileStagedContactsAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteImportCommandAsync(connection, transaction, @"
DELETE existing
FROM DonorContacts existing
JOIN Donors donor ON donor.Id = existing.DonorId
JOIN TempImportDonors stagedDonor ON stagedDonor.Fund = donor.Fund
    AND stagedDonor.IdentityKey = donor.IdentityKey
LEFT JOIN TempImportContacts stagedContact
    ON stagedContact.Fund = stagedDonor.Fund
    AND stagedContact.IdentityKey = stagedDonor.IdentityKey
    AND stagedContact.ContactType = existing.ContactType
    AND stagedContact.NormalizedValue = existing.NormalizedValue
WHERE stagedContact.IdentityKey IS NULL;", cancellationToken);

        return await ExecuteImportCommandAsync(connection, transaction, @"
INSERT INTO DonorContacts (DonorId, ContactType, ContactValue, NormalizedValue)
SELECT d.Id, c.ContactType, c.ContactValue, c.NormalizedValue
FROM TempImportContacts c
JOIN Donors d ON d.Fund = c.Fund AND d.IdentityKey = c.IdentityKey
ON DUPLICATE KEY UPDATE ContactValue = VALUES(ContactValue);", cancellationToken);
    }

    private static async Task<int> ExecuteImportCommandAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = ImportCommandTimeoutSeconds;
        command.CommandText = commandText;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Fund, string IdentityKey)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Fund, string IdentityKey) x, (string Fund, string IdentityKey) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Fund, y.Fund)
                && StringComparer.OrdinalIgnoreCase.Equals(x.IdentityKey, y.IdentityKey);

        public int GetHashCode((string Fund, string IdentityKey) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Fund), StringComparer.OrdinalIgnoreCase.GetHashCode(value.IdentityKey));
    }

    private static async Task<long> UpsertDonorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        DonorWriteModel donor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = ImportCommandTimeoutSeconds;
        command.CommandText = @"
INSERT INTO Donors
    (Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion)
VALUES
    (@Fund, @DisplayName, @IdentityKey, @ResolutionSource, @ResolutionVersion)
AS incoming
ON DUPLICATE KEY UPDATE
    DisplayName = incoming.DisplayName,
    ResolutionSource = incoming.ResolutionSource,
    ResolutionVersion = incoming.ResolutionVersion,
    DateModified = CURRENT_TIMESTAMP;";
        AddDonorParameters(command, donor);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandTimeout = ImportCommandTimeoutSeconds;
        select.CommandText = "SELECT Id FROM Donors WHERE Fund = @Fund AND IdentityKey = @IdentityKey";
        select.Parameters.AddWithValue("@Fund", donor.Fund);
        select.Parameters.AddWithValue("@IdentityKey", donor.IdentityKey);
        var value = await select.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    private static async Task<int> InsertDonationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CanonicalDonationWriteModel donation,
        long donorId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = ImportCommandTimeoutSeconds;
        command.CommandText = @"
INSERT INTO DonationData
    (DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType, Amount, Fund,
     Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName, IsAnonymous, Frequency)
VALUES
    (@DonorId, @Date, @GiftImportId, @AccountName, @PaymentMethod, @GiftType, @Amount, @Fund,
     @Intern, @PrimaryAddressee, @SoftCreditName, @HonorMemorialName, @IsAnonymous, @Frequency);";
        command.Parameters.AddWithValue("@DonorId", donorId);
        command.Parameters.AddWithValue("@Date", donation.Date);
        command.Parameters.AddWithValue("@GiftImportId", (object?)donation.GiftImportId ?? DBNull.Value);
        command.Parameters.AddWithValue("@AccountName", donation.AccountName);
        command.Parameters.AddWithValue("@PaymentMethod", donation.PaymentMethod);
        command.Parameters.AddWithValue("@GiftType", donation.GiftType);
        command.Parameters.AddWithValue("@Amount", donation.Amount);
        command.Parameters.AddWithValue("@Fund", donation.Fund);
        command.Parameters.AddWithValue("@Intern", (object?)donation.Intern ?? DBNull.Value);
        command.Parameters.AddWithValue("@PrimaryAddressee", (object?)donation.PrimaryAddressee ?? DBNull.Value);
        command.Parameters.AddWithValue("@SoftCreditName", (object?)donation.SoftCreditName ?? DBNull.Value);
        command.Parameters.AddWithValue("@HonorMemorialName", (object?)donation.HonorMemorialName ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsAnonymous", donation.IsAnonymous);
        command.Parameters.AddWithValue("@Frequency", donation.Frequency.HasValue ? (object)(int)donation.Frequency.Value : DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var identity = connection.CreateCommand();
        identity.Transaction = transaction;
        identity.CommandTimeout = ImportCommandTimeoutSeconds;
        identity.CommandText = "SELECT LAST_INSERT_ID()";
        var value = await identity.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task<int> ReconcileContactsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long donorId,
        IEnumerable<DonorContactWriteModel> contacts,
        CancellationToken cancellationToken)
    {
        var contactList = contacts
            .GroupBy(contact => $"{contact.ContactType}\u001f{contact.NormalizedValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        foreach (var contactType in ContactTypes)
        {
            var current = contactList
                .Where(contact => string.Equals(contact.ContactType, contactType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandTimeout = ImportCommandTimeoutSeconds;
            delete.CommandText = current.Count == 0
                ? "DELETE FROM DonorContacts WHERE DonorId = @DonorId AND ContactType = @ContactType"
                : $"DELETE FROM DonorContacts WHERE DonorId = @DonorId AND ContactType = @ContactType AND NormalizedValue NOT IN ({string.Join(", ", current.Select((_, index) => $"@Value{index}"))})";
            delete.Parameters.AddWithValue("@DonorId", donorId);
            delete.Parameters.AddWithValue("@ContactType", contactType);
            for (var index = 0; index < current.Count; index++)
            {
                delete.Parameters.AddWithValue($"@Value{index}", current[index].NormalizedValue);
            }

            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var added = 0;
        foreach (var contact in contactList)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = ImportCommandTimeoutSeconds;
            command.CommandText = @"
INSERT INTO DonorContacts
    (DonorId, ContactType, ContactValue, NormalizedValue)
VALUES
    (@DonorId, @ContactType, @ContactValue, @NormalizedValue) AS incoming
ON DUPLICATE KEY UPDATE
    ContactValue = incoming.ContactValue;";
            command.Parameters.AddWithValue("@DonorId", donorId);
            command.Parameters.AddWithValue("@ContactType", contact.ContactType);
            command.Parameters.AddWithValue("@ContactValue", contact.ContactValue);
            command.Parameters.AddWithValue("@NormalizedValue", contact.NormalizedValue);
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    private static void AddDonorParameters(MySqlCommand command, DonorWriteModel donor)
    {
        command.Parameters.AddWithValue("@Fund", donor.Fund);
        command.Parameters.AddWithValue("@DisplayName", donor.DisplayName);
        command.Parameters.AddWithValue("@IdentityKey", donor.IdentityKey);
        command.Parameters.AddWithValue("@ResolutionSource", donor.ResolutionSource);
        command.Parameters.AddWithValue("@ResolutionVersion", donor.ResolutionVersion);
    }

    private static DonorRecord ReadDonor(DbDataReader reader)
    {
        return new DonorRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            Fund = reader.GetString(reader.GetOrdinal("Fund")),
            DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            IdentityKey = reader.GetString(reader.GetOrdinal("IdentityKey")),
            ResolutionSource = reader.GetString(reader.GetOrdinal("ResolutionSource")),
            ResolutionVersion = reader.GetInt32(reader.GetOrdinal("ResolutionVersion"))
        };
    }
}
