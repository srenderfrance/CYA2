using Cya2.Core.DTOs;
using Cya2.Core.Services;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed partial class DonationRepository
{
    private static async Task FinalizeSharedIdentitiesAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteImportCommandAsync(connection, transaction, @"DELETE donor FROM Donors donor
 LEFT JOIN DonationData donation ON donation.DonorId = donor.Id
 WHERE donation.Id IS NULL AND donor.ConstituentId IS NULL", cancellationToken);
        await ExecuteImportCommandAsync(connection, transaction, @"DELETE contact FROM DonorContacts contact
 JOIN Donors donor ON donor.Id = contact.DonorId
 WHERE donor.ConstituentId IS NULL AND NOT EXISTS
 (SELECT 1 FROM DonationData donation WHERE donation.DonorId = donor.Id
 AND donation.IsSoftCredit = 0 AND donation.IsAnonymous = 0)", cancellationToken);
        var records = new List<(int Id, long DonorId, string Fund, DateTime Date, decimal Amount)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, DonorId, Fund, Date, Amount FROM DonationData ORDER BY Date, Id";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                records.Add((reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2), reader.GetDateTime(3), reader.GetDecimal(4)));
        }
        var classifier = new DonorFrequencyService();
        foreach (var group in records.GroupBy(row => (row.DonorId, Fund: row.Fund.Trim().ToUpperInvariant())))
        {
            var history = group.Select(row => new DonorGiftRecord { Date = row.Date, Amount = row.Amount }).ToList();
            foreach (var row in group)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE DonationData SET Frequency = @Frequency WHERE Id = @Id";
                command.Parameters.AddWithValue("@Frequency", (int)classifier.ClassifyGift(new DonorGiftRecord { Date = row.Date, Amount = row.Amount }, history).Frequency);
                command.Parameters.AddWithValue("@Id", row.Id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private sealed record RetainedIdentity(int DonationId, long OldDonorId, string Name, string? SourceId, string? SoftCreditRecipientId, bool SoftCredit, bool Anonymous, string Source, int Version)
    {
        public string Key { get; set; } = string.Empty;
        public string? ConstituentId { get; set; }
    }

    private static async Task<List<RetainedIdentity>> ResolveSharedIdentitiesAsync(
        MySqlConnection connection, IReadOnlyList<CanonicalDonationWriteModel> incoming, CancellationToken cancellationToken)
    {
        var retained = new List<RetainedIdentity>();
        var evidence = new List<ConstituentIdentityEvidence>();
        var resolver = new DonorIdentityResolver();
        var start = incoming.Min(row => row.Date);
        await using (var donors = connection.CreateCommand())
        {
            donors.CommandText = "SELECT DisplayName, ConstituentId FROM Donors WHERE ConstituentId IS NOT NULL";
            await using var reader = await donors.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                evidence.Add(new(reader.GetString(0), reader.GetString(1), false, false));
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT d.Id, d.DonorId, d.AccountName, d.SoftCreditName, d.PrimaryAddressee,
 d.IsAnonymous, d.ConstituentId, d.IsSoftCredit, d.Fund, d.Date, d.SoftCreditRecipientId
 FROM DonationData d";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var input = new DonorIdentityInput(reader.GetString(8), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5));
                var identity = resolver.Resolve(input);
                var row = new RetainedIdentity(reader.GetInt32(0), reader.GetInt64(1), identity.DisplayName,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    resolver.IsThirdPartySoftCredit(input) && !reader.IsDBNull(10) ? reader.GetString(10) : null,
                    reader.GetBoolean(7), reader.GetBoolean(5), identity.ResolutionSource, identity.ResolutionVersion);
                if (reader.GetDateTime(9) < start) retained.Add(row);
                evidence.Add(new(row.Name, row.SourceId, row.SoftCredit, row.Anonymous, row.SoftCreditRecipientId));
            }
        }
        evidence.AddRange(incoming.Select(row => new ConstituentIdentityEvidence(row.Donor.DisplayName, row.ConstituentId, row.IsSoftCredit, row.IsAnonymous,
            resolver.IsThirdPartySoftCredit(new DonorIdentityInput(row.Fund, row.AccountName, row.SoftCreditName, row.PrimaryAddressee, row.IsAnonymous)) ? row.SoftCreditRecipientId : null)));
        var identities = new ConstituentIdentityResolver(evidence);
        foreach (var row in incoming)
        {
            var resolved = identities.Resolve(new(row.Donor.DisplayName, row.ConstituentId, row.IsSoftCredit, row.IsAnonymous,
                resolver.IsThirdPartySoftCredit(new DonorIdentityInput(row.Fund, row.AccountName, row.SoftCreditName, row.PrimaryAddressee, row.IsAnonymous)) ? row.SoftCreditRecipientId : null));
            row.Donor.IdentityKey = resolved.Key;
            row.Donor.ConstituentId = resolved.ConstituentId;
        }
        foreach (var row in retained)
        {
            var resolved = identities.Resolve(new(row.Name, row.SourceId, row.SoftCredit, row.Anonymous, row.SoftCreditRecipientId));
            row.Key = resolved.Key;
            row.ConstituentId = resolved.ConstituentId;
        }
        return retained;
    }

    private static async Task RelinkRetainedDonationsAsync(MySqlConnection connection, MySqlTransaction transaction,
        List<RetainedIdentity> retained, CancellationToken cancellationToken)
    {
        foreach (var group in retained.GroupBy(row => row.Key, StringComparer.Ordinal))
        {
            var row = group.First();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO Donors (IdentityKey, ConstituentId, DisplayName, ResolutionSource, ResolutionVersion)
 VALUES (@Key, @ConstituentId, @Name, @Source, @Version)
 ON DUPLICATE KEY UPDATE Id = LAST_INSERT_ID(Id)";
            command.Parameters.AddWithValue("@Key", row.Key);
            command.Parameters.AddWithValue("@ConstituentId", (object?)row.ConstituentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Name", row.Name);
            command.Parameters.AddWithValue("@Source", row.Source);
            command.Parameters.AddWithValue("@Version", row.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var row in retained)
        {
            if (!row.SoftCredit && !row.Anonymous)
            {
                await using var contacts = connection.CreateCommand();
                contacts.Transaction = transaction;
                contacts.CommandText = @"INSERT INTO DonorContacts (DonorId, ContactType, ContactValue, NormalizedValue, DateCreated, DateModified)
 SELECT target.Id, contact.ContactType, contact.ContactValue, contact.NormalizedValue, contact.DateCreated, contact.DateModified
 FROM DonorContacts contact JOIN Donors original ON original.Id = contact.DonorId
 JOIN Donors target ON target.IdentityKey = @Key
 WHERE original.Id = @Old AND original.Id <> target.Id
 AND (original.ConstituentId IS NULL OR original.ConstituentId = target.ConstituentId)
 ON DUPLICATE KEY UPDATE Id = DonorContacts.Id";
                contacts.Parameters.AddWithValue("@Key", row.Key);
                contacts.Parameters.AddWithValue("@Old", row.OldDonorId);
                await contacts.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"UPDATE DonationData donation JOIN Donors donor ON donor.IdentityKey = @Key
 SET donation.DonorId = donor.Id WHERE donation.Id = @Id";
            command.Parameters.AddWithValue("@Key", row.Key);
            command.Parameters.AddWithValue("@Id", row.DonationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
