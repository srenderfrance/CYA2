using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class SessionDonorSummaryCacheService : ISessionDonorSummaryCacheService
{
    private readonly Dictionary<string, List<DonorSummaryDto>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly ILogger<SessionDonorSummaryCacheService> _logger;

    public SessionDonorSummaryCacheService(ILogger<SessionDonorSummaryCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, out List<DonorSummaryDto> data)
    {
        data = default!;
        var key = BuildKey(fundsSignature, startDate, endDate);
        lock (_sync)
        {
            if (!_cache.TryGetValue(key, out var cached))
            {
                _logger.LogInformation("Donor summary cache miss: funds={Funds}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}", fundsSignature, startDate, endDate);
                return false;
            }

            data = cached;
            _logger.LogInformation("Donor summary cache hit: funds={Funds}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, rows={Rows}", fundsSignature, startDate, endDate, cached.Count);
            return true;
        }
    }

    public void SetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, List<DonorSummaryDto> data)
    {
        var key = BuildKey(fundsSignature, startDate, endDate);
        lock (_sync)
        {
            _cache[key] = data;
        }

        _logger.LogInformation("Donor summary cache set: funds={Funds}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, rows={Rows}", fundsSignature, startDate, endDate, data?.Count ?? 0);
    }

    public void InvalidateAll()
    {
        lock (_sync) { _cache.Clear(); }
        _logger.LogInformation("Donor summary cache invalidated (import/rollback)");
    }

    private static string BuildKey(string fundsSignature, DateTime startDate, DateTime endDate)
        => $"{fundsSignature}|{startDate:yyyyMMdd}|{endDate:yyyyMMdd}";
}
