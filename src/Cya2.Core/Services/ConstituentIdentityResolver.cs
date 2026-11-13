namespace Cya2.Core.Services;

public sealed record ConstituentIdentityEvidence(string Name, string? ConstituentId, bool IsSoftCredit, bool IsAnonymous, string? SoftCreditRecipientId = null)
{
    public string? DonorId => IsSoftCredit ? SoftCreditRecipientId : ConstituentId;
}

public sealed class ConstituentIdentityResolver
{
    private readonly Dictionary<string, HashSet<string>> _idsByName = new(StringComparer.Ordinal);

    public ConstituentIdentityResolver(IEnumerable<ConstituentIdentityEvidence> evidence)
    {
        foreach (var item in evidence.Where(item => !item.IsAnonymous && !string.IsNullOrWhiteSpace(item.DonorId)))
        {
            var name = DonorIdentityResolver.NormalizeIdentityKey(item.Name);
            if (!_idsByName.TryGetValue(name, out var ids)) _idsByName[name] = ids = new(StringComparer.Ordinal);
            ids.Add(item.DonorId!.Trim());
        }
    }

    public (string Key, string? ConstituentId) Resolve(ConstituentIdentityEvidence item)
    {
        if (item.IsAnonymous) return ("anonymous", null);
        var name = DonorIdentityResolver.NormalizeIdentityKey(item.Name);
        if (!string.IsNullOrWhiteSpace(item.DonorId))
        {
            var id = item.DonorId.Trim();
            return ("id:" + id, id);
        }
        if (_idsByName.TryGetValue(name, out var ids) && ids.Count == 1)
        {
            var id = ids.Single();
            return ("id:" + id, id);
        }
        return ("name:" + name, null);
    }
}
