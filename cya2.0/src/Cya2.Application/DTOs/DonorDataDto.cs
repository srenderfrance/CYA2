using System.Collections.Generic;

namespace Cya2.Application.DTOs;

public class DonorDataDto
{
    public List<DonorSummaryDto> Items { get; set; } = new();
    public string SelectedAccount { get; set; } = string.Empty;
}
