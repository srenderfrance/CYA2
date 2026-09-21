namespace Cya2.Core.ReadModels;

public sealed class DonorRecord
{
    public long Id { get; set; }
    public string Fund { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public string ResolutionSource { get; set; } = string.Empty;
    public int ResolutionVersion { get; set; }
}
