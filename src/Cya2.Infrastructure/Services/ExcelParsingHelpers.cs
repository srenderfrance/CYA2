using System;
using System.Globalization;

namespace Cya2.Infrastructure.Services
{
    internal static class ExcelParsingHelpers
    {
        private static readonly CultureInfo UsCulture = new("en-US");

        public static bool TryGetString(OfficeOpenXml.ExcelWorksheet ws, int row, int col, out string value)
        {
            var v = ws.Cells[row, col]?.Text?.Trim();
            value = v ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        public static bool TryParseDateUS(string? input, out DateTime date)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                date = default;
                return false;
            }

            var trimmed = input.Trim();
            
            // Only accept US format (MM/dd/yyyy) to avoid date ambiguity
            // This prevents donations from being incorrectly aggregated due to date misinterpretation
            if (DateTime.TryParse(trimmed, UsCulture, DateTimeStyles.AssumeLocal, out date))
            {
                return true;
            }
            
            date = default;
            return false;
        }

        public static bool TryParseDoubleUS(string? input, out double number)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                number = 0d;
                return false;
            }
            return double.TryParse(input, NumberStyles.Any, UsCulture, out number);
        }

        public static bool ParseYesNo(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            var v = input.Trim();
            // Normalize common representations
            var norm = v.Trim().ToUpperInvariant();

            // Numeric representations
            if (int.TryParse(norm, out var n))
            {
                return n != 0;
            }

            // Common text representations for true
            if (norm == "YES" || norm == "Y" || norm == "TRUE" || norm == "T" || norm == "ON") return true;

            // Also accept localized single-letter true/false if needed
            return false;
        }
    }
}
