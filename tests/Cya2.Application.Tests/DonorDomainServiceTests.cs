using Cya2.Core.Enums;
using Cya2.Core.Entities;
using Cya2.Core.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonorDomainServiceTests
{
    private readonly DonorFrequencyService _frequencyService = new();
    private readonly DonorMissingGiftService _missingGiftService = new();

    [Fact]
    public void ClassifyDonor_ReturnsExpectedFrequencyForRegularPatterns()
    {
        Assert.Equal(DonorFrequency.OneTime, _frequencyService.ClassifyDonor([
            Gift("2026-01-15")
        ]));

        Assert.Equal(DonorFrequency.Monthly, _frequencyService.ClassifyDonor([
            Gift("2026-01-15"), Gift("2026-02-15"), Gift("2026-03-15"), Gift("2026-04-15")
        ]));

        Assert.Equal(DonorFrequency.Quarterly, _frequencyService.ClassifyDonor([
            Gift("2025-01-15"), Gift("2025-04-15"), Gift("2025-07-15")
        ]));

        Assert.Equal(DonorFrequency.Yearly, _frequencyService.ClassifyDonor([
            Gift("2023-01-15"), Gift("2024-01-15"), Gift("2025-01-15")
        ]));
    }

    [Fact]
    public void DetectCatchUpAndPrePayment_ReturnCoveredMonths()
    {
        var history = new[]
        {
            Gift("2026-01-15", 100),
            Gift("2026-02-15", 100),
            Gift("2026-04-15", 100)
        };

        var catchUp = _frequencyService.DetectCatchUp(Gift("2026-04-15", 200), history);
        var prePayment = _frequencyService.DetectPrePayment(Gift("2026-01-15", 200), history);

        Assert.True(catchUp.IsCatchUp);
        Assert.Equal(2, catchUp.CatchUpMonthsCovered);
        Assert.True(prePayment.IsPrePayment);
        Assert.Equal(2, prePayment.MonthsCovered);
    }

    [Fact]
    public void MissingGiftService_ReportsOnlyUncoveredCompletedMonths()
    {
        var history = new[]
        {
            Gift("2026-01-15"),
            Gift("2026-02-15"),
            Gift("2026-04-15")
        };

        var alerts = _missingGiftService.GetMissingGiftAlerts("Donor", history, new DateTime(2026, 5, 20));

        var alert = Assert.Single(alerts);
        Assert.Equal(new DateTime(2026, 3, 1), alert.ExpectedMonth);
        Assert.Equal("Donor", alert.DonorName);
    }

    [Fact]
    public void DonorProfile_UsesCanonicalFrequencyClassification()
    {
        var donations = new List<Donation>
        {
            new(100, new DateTime(2026, 1, 15), "Donor", "FUND", "ACH", "Gift"),
            new(100, new DateTime(2026, 2, 15), "Donor", "FUND", "ACH", "Gift"),
            new(100, new DateTime(2026, 3, 15), "Donor", "FUND", "ACH", "Gift"),
            new(100, new DateTime(2026, 4, 15), "Donor", "FUND", "ACH", "Gift")
        };

        var profile = DonorProfile.CreateFromDonations("Donor", donations);

        var result = profile.GetFrequencyAnalysis(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 4, 30),
            _frequencyService);

        Assert.Equal(DonorFrequency.Monthly, result.Frequency);
        Assert.False(result.HasFrequencyChanged);
        Assert.Empty(result.MissedMonths);
    }

    private static DonorGiftRecord Gift(string date, decimal amount = 100) => new()
    {
        Date = DateTime.Parse(date),
        Amount = amount
    };
}