using System;

namespace UtilityClasses
{
    public static class FundDisplayHelper
    {
        /// <summary>
        /// Returns the portion of the fund string before the first colon, trimming trailing whitespace.
        /// Example: "Name Part A : Code123" => "Name Part A"
        /// </summary>
        public static string GetDisplay(string? fund)
        {
            if (string.IsNullOrWhiteSpace(fund)) return string.Empty;

            // Prefer space-colon pattern if present, otherwise any colon
            var idx = fund.IndexOf(" :", StringComparison.Ordinal);
            if (idx < 0)
            {
                idx = fund.IndexOf(':');
            }

            var before = idx >= 0 ? fund.Substring(0, idx) : fund;
            return before.TrimEnd();
        }
    }
}
