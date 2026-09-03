using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public class DonationPresentationService : IDonationPresentationService
{
    public List<DonationRowDto> FilterByDateRange(List<DonationRowDto> data, DateTime? start, DateTime? end)
    {
        if (data == null) return new List<DonationRowDto>();
        if (start == null && end == null) return data;
        if (start != null && end == null) return data.Where(d => d.Date >= start).ToList();
        if (start == null && end != null) return data.Where(d => d.Date <= end).ToList();
        return data.Where(d => d.Date >= start && d.Date <= end).ToList();
    }

    public DonationPivotResultDto BuildPivot(List<DonationRowDto> selectedDonations, DateTime startDate, DateTime endDate, string unknownDonorLabel)
    {
        var result = new DonationPivotResultDto();

        var start = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        for (var cursor = start; cursor <= end; cursor = cursor.AddMonths(1))
        {
            result.MonthColumns.Add(cursor);
        }

        if (selectedDonations == null || !selectedDonations.Any())
        {
            return result;
        }

        var byDonor = selectedDonations.GroupBy(d => d.Donor).OrderBy(g => g.Key ?? string.Empty);

        foreach (var g in byDonor)
        {
            var row = new DonationPivotRowDto
            {
                Donor = g.Key ?? unknownDonorLabel,
                IsAnonymous = g.All(d => d.IsAnonymous)
            };

            foreach (var donation in g)
            {
                var monthKey = new DateTime(donation.Date.Year, donation.Date.Month, 1);
                if (!row.Monthly.ContainsKey(monthKey))
                {
                    row.Monthly[monthKey] = 0m;
                }
                row.Monthly[monthKey] += Convert.ToDecimal(donation.Amount);
            }

            result.Rows.Add(row);
        }

        foreach (var month in result.MonthColumns)
        {
            decimal total = 0m;
            foreach (var row in result.Rows)
            {
                if (row.Monthly.TryGetValue(month, out var amt))
                {
                    total += amt;
                }
            }
            result.MonthTotals[month] = total;
        }

        result.GrandTotal = result.MonthTotals.Values.Sum();
        return result;
    }
}
