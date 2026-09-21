namespace Cya2.Core.ReadModels;

public sealed class DonorContactRecord
{
    public long Id { get; set; }
    public long DonorId { get; set; }
    public string ContactType { get; set; } = string.Empty;
    public string ContactValue { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
}
