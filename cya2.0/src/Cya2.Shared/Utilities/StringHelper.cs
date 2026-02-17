namespace Cya2.Shared.Utilities;

/// <summary>
/// Static utility class for string manipulation operations.
/// Contains pure string processing functions.
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// Get the first non-empty string from a collection
    /// </summary>
    public static string GetFirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Get the first non-empty string from an enumerable
    /// </summary>
    public static string GetFirstNonEmpty(IEnumerable<string?> values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Join strings with a separator, ignoring null/empty values
    /// </summary>
    public static string JoinNonEmpty(string separator, params string?[] values)
    {
        var nonEmptyValues = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim());
        return string.Join(separator, nonEmptyValues);
    }

    /// <summary>
    /// Join strings with a separator, ignoring null/empty values
    /// </summary>
    public static string JoinNonEmpty(string separator, IEnumerable<string?> values)
    {
        var nonEmptyValues = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim());
        return string.Join(separator, nonEmptyValues);
    }

    /// <summary>
    /// Clean and normalize a string by trimming and removing extra whitespace
    /// </summary>
    public static string CleanString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Replace multiple whitespace with single space and trim
        return System.Text.RegularExpressions.Regex.Replace(input.Trim(), @"\s+", " ");
    }

    /// <summary>
    /// Extract initials from a full name
    /// </summary>
    public static string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Where(w => w.Length > 0).Select(w => char.ToUpper(w[0])));
    }

    /// <summary>
    /// Convert string to title case
    /// </summary>
    public static string ToTitleCase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(input.ToLower());
    }

    /// <summary>
    /// Mask sensitive information (credit card, SSN, etc.)
    /// </summary>
    public static string MaskSensitiveData(string? input, int visibleChars = 4, char maskChar = '*')
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length <= visibleChars)
            return input ?? string.Empty;

        var maskedPortion = new string(maskChar, input.Length - visibleChars);
        var visiblePortion = input[^visibleChars..];
        return maskedPortion + visiblePortion;
    }

    /// <summary>
    /// Generate a URL-friendly slug from a string
    /// </summary>
    public static string ToSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Convert to lowercase and replace spaces with hyphens
        var slug = input.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }

    /// <summary>
    /// Check if a string contains any of the specified search terms (case-insensitive)
    /// </summary>
    public static bool ContainsAny(string? input, params string[] searchTerms)
    {
        if (string.IsNullOrWhiteSpace(input) || !searchTerms.Any())
            return false;

        return searchTerms.Any(term => !string.IsNullOrWhiteSpace(term) && 
                                     input.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ellipsize text at word boundaries
    /// </summary>
    public static string EllipsizeAtWords(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length <= maxLength)
            return input ?? string.Empty;

        var trimmed = input[..maxLength];
        var lastSpace = trimmed.LastIndexOf(' ');
        
        if (lastSpace > maxLength / 2) // Only break at word if space is reasonably close to end
            trimmed = trimmed[..lastSpace];
        
        return trimmed + "...";
    }

    /// <summary>
    /// Remove diacritics (accents) from text
    /// </summary>
    public static string RemoveDiacritics(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalizedString = input.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}