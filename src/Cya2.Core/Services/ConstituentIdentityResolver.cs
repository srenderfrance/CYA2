namespace Cya2.Core.Services;

public sealed record ConstituentIdentityEvidence(string Name, string? ConstituentId, bool IsSoftCredit, bool IsAnonymous);

public sealed class ConstituentIdentityResolver
{
    private readonly Dictionary<string, HashSet<string>> _idsByName = new(StringComparer.Ordinal);

    public ConstituentIdentityResolver(IEnumerable<ConstituentIdentityEvidence> evidence)
    {
        foreach (var item in evidence.Where(item => !item.IsAnonymous && !item.IsSoftCredit && !string.IsNullOrWhiteSpace(item.ConstituentId)))
        {
            var name = DonorIdentityResolver.NormalizeIdentityKey(item.Name);
            if (!_idsByName.TryGetValue(name, out var ids)) _idsByName[name] = ids = new(StringComparer.Ordinal);
            ids.Add(item.ConstituentId!.Trim());
        }
    }

    public (string Key, string? ConstituentId) Resolve(ConstituentIdentityEvidence item)
    {
        if (item.IsAnonymous) return ("anonymous", null);
        var name = DonorIdentityResolver.NormalizeIdentityKey(item.Name);
        if (!item.IsSoftCredit && !string.IsNullOrWhiteSpace(item.ConstituentId))
        {
            var id = item.ConstituentId.Trim();
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
