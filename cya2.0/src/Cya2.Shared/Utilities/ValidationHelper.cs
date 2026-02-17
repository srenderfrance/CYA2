using System.Text.RegularExpressions;

namespace Cya2.Shared.Utilities;

/// <summary>
/// Static utility class for data validation.
/// Contains pure validation functions with no dependencies.
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validate email address format
    /// </summary>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validate US phone number format (10 or 11 digits)
    /// </summary>
    public static bool IsValidPhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = Regex.Replace(phone, @"\D", "");
        return digits.Length == 10 || (digits.Length == 11 && digits.StartsWith("1"));
    }

    /// <summary>
    /// Validate US postal code (5 digits or 5+4 format)
    /// </summary>
    public static bool IsValidUSPostalCode(string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
            return false;

        return Regex.IsMatch(postalCode, @"^\d{5}(-\d{4})?$");
    }

    /// <summary>
    /// Validate that date range is valid (start <= end)
    /// </summary>
    public static bool IsValidDateRange(DateTime startDate, DateTime endDate)
    {
        return startDate <= endDate;
    }

    /// <summary>
    /// Validate fund code format (basic validation)
    /// </summary>
    public static bool IsValidFundCode(string? fundCode)
    {
        if (string.IsNullOrWhiteSpace(fundCode))
            return false;

        // Basic validation: at least 2 characters, alphanumeric with some symbols
        return fundCode.Length >= 2 && Regex.IsMatch(fundCode, @"^[A-Za-z0-9\-_\.:]+$");
    }

    /// <summary>
    /// Validate that string is not null/empty/whitespace
    /// </summary>
    public static bool IsRequired(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Validate string length is within range
    /// </summary>
    public static bool IsValidLength(string? value, int minLength, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return minLength == 0;

        return value.Length >= minLength && value.Length <= maxLength;
    }

    /// <summary>
    /// Validate that value is within numeric range
    /// </summary>
    public static bool IsInRange(double value, double min, double max)
    {
        return value >= min && value <= max;
    }

    /// <summary>
    /// Validate that date is within reasonable range for donations
    /// </summary>
    public static bool IsValidDonationDate(DateTime date)
    {
        // Allow dates from 1900 to future (up to 1 year ahead for pledges)
        var minDate = new DateTime(1900, 1, 1);
        var maxDate = DateTime.Today.AddYears(1);
        return date >= minDate && date <= maxDate;
    }
}