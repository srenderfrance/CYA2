namespace Cya2.Shared.Extensions;

/// <summary>
/// Extension methods for common .NET types
/// </summary>
public static class CommonExtensions
{
    /// <summary>
    /// Check if a string is null, empty, or whitespace
    /// </summary>
    public static bool IsNullOrEmpty(this string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Safe substring that won't throw if indices are out of range
    /// </summary>
    public static string SafeSubstring(this string? value, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(value) || startIndex >= value.Length)
            return string.Empty;

        if (startIndex + length > value.Length)
            length = value.Length - startIndex;

        return startIndex < 0 ? string.Empty : value.Substring(startIndex, length);
    }

    /// <summary>
    /// Convert nullable decimal to currency string with fallback
    /// </summary>
    public static string ToCurrencyString(this decimal? value, string fallback = "-")
    {
        return value?.ToString("C2") ?? fallback;
    }

    /// <summary>
    /// Convert decimal to currency string
    /// </summary>
    public static string ToCurrencyString(this decimal value)
    {
        return value.ToString("C2");
    }

    /// <summary>
    /// Check if a date is within a range (inclusive)
    /// </summary>
    public static bool IsBetween(this DateTime date, DateTime start, DateTime end)
    {
        return date.Date >= start.Date && date.Date <= end.Date;
    }

    /// <summary>
    /// Get the start of the day for a DateTime
    /// </summary>
    public static DateTime StartOfDay(this DateTime date)
    {
        return date.Date;
    }

    /// <summary>
    /// Get the end of the day for a DateTime
    /// </summary>
    public static DateTime EndOfDay(this DateTime date)
    {
        return date.Date.AddDays(1).AddTicks(-1);
    }

    /// <summary>
    /// Check if a collection is null or empty
    /// </summary>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? collection)
    {
        return collection == null || !collection.Any();
    }

    /// <summary>
    /// Safe FirstOrDefault with null collection handling
    /// </summary>
    public static T? SafeFirstOrDefault<T>(this IEnumerable<T>? collection, Func<T, bool>? predicate = null)
    {
        if (collection == null)
            return default(T);

        return predicate == null ? collection.FirstOrDefault() : collection.FirstOrDefault(predicate);
    }

    /// <summary>
    /// Convert string to enum with fallback value
    /// </summary>
    public static TEnum ToEnumOrDefault<TEnum>(this string? value, TEnum defaultValue = default) 
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return Enum.TryParse<TEnum>(value, true, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Chunk a collection into smaller collections of specified size
    /// </summary>
    public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int chunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentException("Chunk size must be greater than 0", nameof(chunkSize));

        var chunk = new List<T>(chunkSize);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count == chunkSize)
            {
                yield return chunk;
                chunk = new List<T>(chunkSize);
            }
        }

        if (chunk.Count > 0)
            yield return chunk;
    }

    /// <summary>
    /// Get distinct items by a key selector
    /// </summary>
    public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        var seenKeys = new HashSet<TKey>();
        foreach (var item in source)
        {
            var key = keySelector(item);
            if (seenKeys.Add(key))
                yield return item;
        }
    }
}