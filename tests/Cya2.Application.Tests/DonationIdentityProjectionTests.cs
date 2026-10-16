using Cya2.Application.DTOs;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonationIdentityProjectionTests
{
    [Fact]
    public void DonationRowsCanUseDonorIdWhenDisplayNamesAreNotUnique()
    {
        var data = new DonationDataDto
        {
            Donations =
            [
                new DonationRowDto { DonorId = 10, Donor = "Alex Smith" },
                new DonationRowDto { DonorId = 20, Donor = "Alex Smith" }
            ],
            DonorContacts = new Dictionary<long, DonorContactDto>
            {
                [10] = new() { Email = "alex.one@example.test" },
                [20] = new() { Email = "alex.two@example.test" }
            }
        };

        Assert.Equal("alex.one@example.test", data.DonorContacts[data.Donations[0].DonorId].Email);
        Assert.Equal("alex.two@example.test", data.DonorContacts[data.Donations[1].DonorId].Email);
    }
}
