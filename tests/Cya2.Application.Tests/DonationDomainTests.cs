using Cya2.Core.Entities;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonationDomainTests
{
    [Fact]
    public void AnonymousDonation_ClearsPersonalDataAndPreservesAnonymousIdentity()
    {
        var donation = new Donation(100, new DateTime(2026, 1, 15), "Donor", "FUND", "ACH", "Gift", true);
        donation.SetSoftCredit("Organization");
        donation.MarkAsAnonymous();

        Assert.True(donation.IsAnonymous);
        Assert.Equal("Anonymous", donation.AccountName);
        Assert.False(donation.HasAnyPersonalData());
        Assert.Null(donation.Email);
        Assert.Null(donation.SoftCreditName);
    }

    [Fact]
    public void DonorProfile_ProvidesSummaryStatisticsFromCompleteHistory()
    {
        var donations = new List<Donation>
        {
            new(100, new DateTime(2026, 1, 1, 23, 59, 59), "Donor", "FUND", "ACH", "Gift"),
            new(50, new DateTime(2026, 1, 2), "Donor", "FUND", "ACH", "Gift")
        };
        var profile = DonorProfile.CreateFromDonations("Donor", donations);

        Assert.Equal(150m, profile.TotalGiving);
        Assert.Equal(new DateTime(2026, 1, 1, 23, 59, 59), profile.FirstGiftDate);
        Assert.Equal(new DateTime(2026, 1, 2), profile.LastGiftDate);
        Assert.Equal(2, profile.TotalGifts);
        Assert.Equal(75m, profile.AverageGift);
    }
}