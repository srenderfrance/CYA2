using System;
using System.Globalization;

namespace cya2.Services.Imports
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
            return DateTime.TryParse(input, UsCulture, DateTimeStyles.AssumeLocal, out date);
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
            return v.Equals("YES", StringComparison.OrdinalIgnoreCase) || v.Equals("Y", StringComparison.OrdinalIgnoreCase) || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
