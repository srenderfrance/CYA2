using System.Globalization;
using System.Linq;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Enums;
using Cya2.Core.ValueObjects;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Donor management service implementation backed by legacy Donations table.
/// </summary>
public class DonorService : IDonorService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DonorService> _logger;

    public DonorService(IDataAccess dataAccess, IConfiguration configuration, ILogger<DonorService> logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<string>> GetDonorNamesAsync(string accountFund)
    {
        return new List<string>();
    }

    public async Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange)
    {
        // Return empty list for now
        return new List<DonorSummaryDto>();
    }

    public async Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund)
    {
        return null;
    }

    public async Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund)
    {
        return string.Empty;
    }

    public async Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund)
    {
        return new List<DonorSummaryDto>();
    }

    public async Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, string address, string city, string state, string postal, string country)
    {
        return;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("default") ?? string.Empty;
    }
}