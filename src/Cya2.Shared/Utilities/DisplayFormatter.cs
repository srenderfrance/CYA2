using System.Text.RegularExpressions;

namespace Cya2.Shared.Utilities;

/// <summary>
/// Static utility class for formatting display values.
/// Contains pure functions with no dependencies.
/// </summary>
public static class DisplayFormatter
{
    /// <summary>
    /// Format a nullable date as MM/dd/yyyy or dash if null
    /// </summary>
    public static string FormatDate(DateTime? date)
    {
        return date.HasValue ? date.Value.ToString("MM/dd/yyyy") : "-";
    }

    /// <summary>
    /// Format date with custom format or dash if null
    /// </summary>
    public static string FormatDate(DateTime? date, string format)
    {
        return date.HasValue ? date.Value.ToString(format) : "-";
    }

    /// <summary>
    /// Format currency amount with standard currency formatting
    /// </summary>
    public static string FormatCurrency(decimal amount)
    {
        return amount.ToString("C2");
    }

    /// <summary>
    /// Format currency amount with specific culture
    /// </summary>
    public static string FormatCurrency(decimal amount, string cultureName)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        return amount.ToString("C2", culture);
    }

    /// <summary>
    /// Format phone number with standard US formatting if possible
    /// </summary>
    public static string FormatPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) 
            return string.Empty;

        // Remove all non-digits
        var digits = Regex.Replace(phone, @"\D", "");
        if (string.IsNullOrEmpty(digits)) 
            return phone.Trim();

        // Handle country code 1
        if (digits.Length == 11 && digits.StartsWith("1"))
        {
            digits = digits[1..];
        }

        // Format 10-digit numbers as (XXX) XXX-XXXX
        if (digits.Length == 10)
        {
            return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
        }

        // For other lengths, add dash before last 4 digits if possible
        if (digits.Length > 6)
        {
            return digits.Insert(digits.Length - 4, "-");
        }

        return digits;
    }

    /// <summary>
    /// Format a percentage value
    /// </summary>
    public static string FormatPercentage(decimal percentage, int decimalPlaces = 1)
    {
        return $"{Math.Round(percentage, decimalPlaces)}%";
    }

    /// <summary>
    /// Format a large number with appropriate suffixes (K, M, B)
    /// </summary>
    public static string FormatLargeNumber(decimal number)
    {
        return number switch
        {
            >= 1000000000 => $"{number / 1000000000:F1}B",
            >= 1000000 => $"{number / 1000000:F1}M",
            >= 1000 => $"{number / 1000:F1}K",
            _ => number.ToString("F0")
        };
    }

    /// <summary>
    /// Format a fund code by extracting display name before colon separator
    /// </summary>
    public static string FormatFundDisplay(string? fund)
    {
        if (string.IsNullOrWhiteSpace(fund)) 
            return string.Empty;

        // Prefer space-colon pattern if present, otherwise any colon
        var idx = fund.IndexOf(" :", StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = fund.IndexOf(':');
        }

        var before = idx >= 0 ? fund[..idx] : fund;
        return before.TrimEnd();
    }

    /// <summary>
    /// Truncate text to specified length with ellipsis
    /// </summary>
    public static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Format file size in human-readable format
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        var order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}