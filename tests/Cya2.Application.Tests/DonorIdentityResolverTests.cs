using Cya2.Core.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonorIdentityResolverTests
{
    private readonly DonorIdentityResolver _resolver = new();

    [Fact]
    public void Resolve_UsesPrimaryAddresseeForFamilyHousehold()
    {
        var result = _resolver.Resolve(new DonorIdentityInput(
            "GENERAL",
            "John Smith",
            "Jane Smith",
            "John and Jane Smith"));

        Assert.Equal("John and Jane Smith", result.DisplayName);
        Assert.Equal("PrimaryAddressee", result.ResolutionSource);
    }

    [Fact]
    public void Resolve_UsesSoftCreditWhenItIsNotRepresentedByAddressee()
    {
        var result = _resolver.Resolve(new DonorIdentityInput(
            "GENERAL",
            "Employer Giving",
            "Jane Smith",
            "Employer Giving"));

        Assert.Equal("Jane Smith", result.DisplayName);
        Assert.Equal("SoftCredit", result.ResolutionSource);
    }

    [Fact]
    public void Resolve_FallsBackToAccountName()
    {
        var result = _resolver.Resolve(new DonorIdentityInput("GENERAL", "Smith Family", null, null));

        Assert.Equal("Smith Family", result.DisplayName);
        Assert.Equal("AccountName", result.ResolutionSource);
    }

    [Fact]
    public void Resolve_NormalizesLastFirstNamesBeforeCreatingIdentity()
    {
        var result = _resolver.Resolve(new DonorIdentityInput("GENERAL", "Smith, John", null, null));

        Assert.Equal("John Smith", result.DisplayName);
        Assert.Equal("johnsmith", result.IdentityKey);
    }

    [Fact]
    public void Resolve_AnonymousDoesNotExposeSourceIdentity()
    {
        var result = _resolver.Resolve(new DonorIdentityInput("GENERAL", "John Smith", "Jane Smith", "John and Jane Smith", true));

        Assert.Equal("Anonymous", result.DisplayName);
        Assert.Equal("anonymous", result.IdentityKey);
    }
}
