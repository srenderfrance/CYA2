using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class DonorContactReadRepository : IDonorContactReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public DonorContactReadRepository(
        IConfiguration configuration,
        IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<DonorContactRecord>> GetByDonorIdsAsync(
        IEnumerable<long> donorIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (donorIds ?? Enumerable.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new List<DonorContactRecord>();
        }

        _dbGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(ConnStr);
        var command = new CommandDefinition(
            """
            SELECT Id, DonorId, ContactType, ContactValue, NormalizedValue
            FROM DonorContacts
            WHERE DonorId IN @DonorIds
            ORDER BY DonorId, ContactType, Id
            """,
            new { DonorIds = ids },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<DonorContactRecord>(command);
        return rows.ToList();
    }
}
