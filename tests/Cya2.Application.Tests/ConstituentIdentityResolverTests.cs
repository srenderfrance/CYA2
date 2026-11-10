using Cya2.Core.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ConstituentIdentityResolverTests
{
    [Fact]
    public void DirectIdsAreAuthoritativeAndSoftCreditOrganizationIdsAreIgnored()
    {
        var resolver = new ConstituentIdentityResolver([
            new("Smith, Alex", "000123", false, false),
            new("Alex Smith", "employer", true, false),
            new("Other Donor", "employer", true, false)]);
        Assert.Equal(("id:000123", "000123"), resolver.Resolve(new("Alex Smith", "employer", true, false)));
        Assert.Equal(("name:otherdonor", (string?)null), resolver.Resolve(new("Other Donor", "employer", true, false)));
        Assert.Equal(("id:456", "456"), resolver.Resolve(new("Alex Smith", "456", false, false)));
    }

    [Fact]
    public void AmbiguousNamesStaySeparateAndAnonymousDoesNotLink()
    {
        var resolver = new ConstituentIdentityResolver([
            new("Alex Smith", "1", false, false), new("Smith, Alex", "2", false, false)]);
        Assert.Equal(("name:alexsmith", (string?)null), resolver.Resolve(new("Alex Smith", "employer", true, false)));
        Assert.Equal(("anonymous", (string?)null), resolver.Resolve(new("Alex Smith", "1", false, true)));
    }
}
