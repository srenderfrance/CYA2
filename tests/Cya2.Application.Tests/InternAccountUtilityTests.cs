using Cya2.Core.Utilities;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class InternAccountUtilityTests
{
    [Fact]
    public void CreateDesignationCriteria_UsesCanonicalNameAndAlternateForm()
    {
        var criteria = InternAccountUtility.CreateDesignationCriteria("Jane Doe");

        Assert.Equal("Jane Doe", criteria.InternDesignationName);
        Assert.Equal("Doe, Jane", criteria.AlternateDesignation);
        Assert.True(criteria.HasAlternateDesignation);
        Assert.True(criteria.HasNameTokens);
        Assert.Equal("Jane", criteria.FirstName);
        Assert.Equal("Doe", criteria.LastName);
        Assert.Equal("janedoe", criteria.DesignationLookupKey);
        Assert.Equal("doejane", criteria.AlternateLookupKey);
    }

    [Fact]
    public void CreateDesignationCriteria_ParsesCommaReversedName()
    {
        var criteria = InternAccountUtility.CreateDesignationCriteria("Doe, Jane");

        Assert.Equal("Jane Doe", criteria.AlternateDesignation);
        Assert.Equal("Jane", criteria.FirstName);
        Assert.Equal("Doe", criteria.LastName);
        Assert.Equal("doejane", criteria.DesignationLookupKey);
        Assert.Equal("janedoe", criteria.AlternateLookupKey);
    }

    [Fact]
    public void CreateDesignationCriteria_NormalizesWhitespaceAndPunctuationForLookup()
    {
        var criteria = InternAccountUtility.CreateDesignationCriteria("  Jane   Marie Doe  ");

        Assert.Equal("Jane Marie Doe", criteria.InternDesignationName);
        Assert.Equal("janemariedoe", criteria.DesignationLookupKey);
        Assert.Equal("doejanemarie", criteria.AlternateLookupKey);
        Assert.Equal("Jane", criteria.FirstName);
        Assert.Equal("Doe", criteria.LastName);
    }

    [Fact]
    public void CreateDesignationCriteria_HandlesSingleWordNameWithoutAlternateOrTokens()
    {
        var criteria = InternAccountUtility.CreateDesignationCriteria("  Intern  ");

        Assert.Equal("Intern", criteria.InternDesignationName);
        Assert.False(criteria.HasAlternateDesignation);
        Assert.False(criteria.HasNameTokens);
        Assert.False(criteria.HasAlternateLookupKey);
        Assert.Equal("intern", criteria.DesignationLookupKey);
    }
}
