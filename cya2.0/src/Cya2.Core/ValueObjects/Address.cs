namespace Cya2.Core.ValueObjects;

public class Address
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string PostalCode { get; }
    public string Country { get; }

    public Address(string street, string city, string state, string postalCode, string country)
    {
        Street = street?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        State = state?.Trim() ?? string.Empty;
        PostalCode = postalCode?.Trim() ?? string.Empty;
        Country = country?.Trim() ?? string.Empty;
    }

    public bool HasAnyInfo()
    {
        return !string.IsNullOrWhiteSpace(Street) ||
               !string.IsNullOrWhiteSpace(City) ||
               !string.IsNullOrWhiteSpace(State) ||
               !string.IsNullOrWhiteSpace(PostalCode);
    }

    public string GetDisplayAddress()
    {
        var parts = new[] { Street, City, State, PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
            
        return string.Join(", ", parts);
    }

    public override string ToString() => GetDisplayAddress();

    public override bool Equals(object? obj)
    {
        return obj is Address other &&
               Street == other.Street &&
               City == other.City &&
               State == other.State &&
               PostalCode == other.PostalCode &&
               Country == other.Country;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Street, City, State, PostalCode, Country);
    }
}