namespace Cya2.Application.Models;

public readonly record struct AccountSnapshotKey(int AccountId, string Fund, long Generation)
{
    public AccountSnapshotKey Normalize()
        => this with { Fund = Fund?.Trim() ?? string.Empty };
}
