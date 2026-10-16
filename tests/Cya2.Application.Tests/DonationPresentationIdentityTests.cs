using Cya2.Application.DTOs;
using Cya2.Application.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonationPresentationIdentityTests
{
    [Fact]
    public void BuildPivot_GroupsDuplicateDisplayNamesByDonorId()
    {
        var service = new DonationPresentationService();
        var donations = new List<DonationRowDto>
        {
            new() { DonorId = 10, Donor = "Alex Smith", Date = new DateTime(2025, 1, 15), Amount = 10 },
            new() { DonorId = 20, Donor = "Alex Smith", Date = new DateTime(2025, 1, 16), Amount = 20 }
        };

        var result = service.BuildPivot(
            donations,
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 31),
            "Unknown");

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal("Alex Smith", row.Donor));
        Assert.Contains(result.Rows, row => row.DonorId == 10 && row.Total == 10);
        Assert.Contains(result.Rows, row => row.DonorId == 20 && row.Total == 20);
    }
}
