using Cya2.Core.Enums;

namespace Cya2.Core.ValueObjects;

public class ContactInfo
{
    public string? Email { get; }
    public string? PhoneMobile { get; }
    public string? PhoneFixed { get; }
    public Address? Address { get; }
    public DateTime LastUpdated { get; }

    private ContactInfo(string? email, string? phoneMobile, string? phoneFixed, Address? address, DateTime lastUpdated)
    {
        Email = email;
        PhoneMobile = phoneMobile;
        PhoneFixed = phoneFixed;
        Address = address;
        LastUpdated = lastUpdated;
    }

    public static ContactInfo FromDonation(Entities.Donation donation)
    {
        var address = !string.IsNullOrWhiteSpace(donation.Address) || 
                     !string.IsNullOrWhiteSpace(donation.City) || 
                     !string.IsNullOrWhiteSpace(donation.State)
            ? new Address(donation.Address ?? string.Empty, 
                         donation.City ?? string.Empty, 
                         donation.State ?? string.Empty, 
                         donation.PostalCode ?? string.Empty, 
                         donation.Country ?? string.Empty)
            : null;

        return new ContactInfo(
            donation.Email,
            donation.PhoneMobile,
            donation.PhoneFixed,
            address,
            donation.Date);
    }

    public bool HasAnyContactInfo()
    {
        return !string.IsNullOrWhiteSpace(Email) ||
               !string.IsNullOrWhiteSpace(PhoneMobile) ||
               !string.IsNullOrWhiteSpace(PhoneFixed) ||
               Address?.HasAnyInfo() == true;
    }

    public string GetPrimaryContact()
    {
        if (!string.IsNullOrWhiteSpace(Email)) return Email;
        if (!string.IsNullOrWhiteSpace(PhoneMobile)) return PhoneMobile;
        if (!string.IsNullOrWhiteSpace(PhoneFixed)) return PhoneFixed;
        return "No contact info";
    }
}