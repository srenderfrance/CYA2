using Cya2.Core.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonationDuplicateClassifierTests
{
    [Fact]
    public void IsSameAllocation_IgnoresContactFieldsBecauseTheyAreNotPartOfCandidate()
    {
        var first = Candidate("first@example.com", "555-1111");
        var second = Candidate("second@example.com", "555-2222");

        Assert.True(DonationDuplicateClassifier.IsSameAllocation(first, second));
    }

    [Fact]
    public void IsSameAllocation_DoesNotMergeDifferentFunds()
    {
        var first = Candidate("first@example.com", "555-1111", "GENERAL");
        var second = Candidate("second@example.com", "555-2222", "BUILDING");

        Assert.False(DonationDuplicateClassifier.IsSameAllocation(first, second));
    }

    [Fact]
    public void IsSameAllocation_DoesNotClassifyRowsWithoutGiftImportId()
    {
        var first = Candidate(null, "555-1111");
        var second = Candidate(null, "555-2222");

        Assert.False(DonationDuplicateClassifier.IsSameAllocation(first, second));
    }

    [Fact]
    public void IsSameAllocation_NormalizesDateAndAmount()
    {
        var first = new DonationDuplicateCandidate("G-1", "GENERAL", new DateTime(2026, 1, 10, 8, 0, 0), 100.004m, "ACH", "Gift", "johnsmith");
        var second = new DonationDuplicateCandidate(" g-1 ", " general ", new DateTime(2026, 1, 10, 18, 0, 0), 100.00m, " ach ", " gift ", "johnsmith");

        Assert.True(DonationDuplicateClassifier.IsSameAllocation(first, second));
    }

    private static DonationDuplicateCandidate Candidate(string? email, string phone, string fund = "GENERAL")
    {
        _ = email;
        _ = phone;
        return new DonationDuplicateCandidate(email == null ? null : "G-1", fund, new DateTime(2026, 1, 10), 100m, "ACH", "Gift", "johnsmith");
    }
}
