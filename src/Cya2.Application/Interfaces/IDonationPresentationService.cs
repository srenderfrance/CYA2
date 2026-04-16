using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IDonationPresentationService
{
    List<DonationRowDto> FilterByDateRange(List<DonationRowDto> data, DateTime? start, DateTime? end);
    DonationPivotResultDto BuildPivot(List<DonationRowDto> selectedDonations, DateTime startDate, DateTime endDate, string unknownDonorLabel);
}
