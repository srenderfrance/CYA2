using ModelsLibrary;
using System.Text.RegularExpressions;

namespace UtilityClasses
{
    public static class DonorDisplayHelper
    {
        // Format nullable date as MM/dd/yyyy or - if null
        public static string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("MM/dd/yyyy") : "-";
        }

        // Normalize and nicely format phone numbers if possible
        public static string FormatPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

            // Remove non digits
            var digits = Regex.Replace(phone, "\\D", "");
            if (string.IsNullOrEmpty(digits)) return phone.Trim();

            // Handle country code 1
            if (digits.Length == 11 && digits.StartsWith("1"))
            {
                digits = digits.Substring(1);
            }

            if (digits.Length == 10)
            {
                return $"({digits.Substring(0,3)}) {digits.Substring(3,3)}-{digits.Substring(6,4)}";
            }

            // If other lengths, return cleaned grouping for readability
            if (digits.Length > 6)
            {
                return digits.Insert(digits.Length - 4, "-");
            }

            return digits;
        }

        // Get primary phone for a donation row (prefer mobile)
        public static string GetPrimaryPhone(DonationsDataModel d)
        {
            if (d == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(d.PhoneMobile)) return FormatPhone(d.PhoneMobile);
            if (!string.IsNullOrWhiteSpace(d.PhoneFixed)) return FormatPhone(d.PhoneFixed);
            return string.Empty;
        }

        // Build address lines from a donation row
        public static (string line1, string line2) GetAddressLines(DonationsDataModel d)
        {
            if (d == null) return (string.Empty, string.Empty);
            var line1 = d.Address?.Trim() ?? string.Empty;
            var parts = new[] { d.City?.Trim(), d.State?.Trim(), d.PostalCode?.Trim() };
            var line2 = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return (line1, line2);
        }
    }
}
