using Cya2.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Cya2.Infrastructure.Services
{
    public class MemoryUserSelectionService : IUserSelectionService
    {
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(24);
        private readonly ILogger<MemoryUserSelectionService> _logger;

        public MemoryUserSelectionService(IMemoryCache cache, ILogger<MemoryUserSelectionService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public void SetSelectedAccount(string userId, string account, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(account))
            {
                _logger.LogDebug("MemoryUserSelectionService.SetSelectedAccount called with empty userId or account. userId='{UserId}', account='{Account}'", userId, account);
                return;
            }
            var key = UserKey(userId);
            if (_cache.TryGetValue(key, out string? existing) && string.Equals(existing, account, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("MemoryUserSelectionService: Selection unchanged for {UserId} -> {Account}; skipping write", userId, account);
                return;
            }
            var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? _defaultTtl };
            _cache.Set(key, account, options);
            _logger.LogInformation("MemoryUserSelectionService: Set selection for {UserId} -> {Account} (ttl={Ttl})", userId, account, options.AbsoluteExpirationRelativeToNow);
        }

        public bool TryGetSelectedAccount(string userId, out string account)
        {
            account = string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogDebug("MemoryUserSelectionService.TryGetSelectedAccount called with empty userId");
                return false;
            }
            var key = UserKey(userId);
            var found = _cache.TryGetValue<string?>(key, out var cachedAccount);
            account = cachedAccount ?? string.Empty;
            _logger.LogInformation("MemoryUserSelectionService: TryGet for {UserId} -> found={Found}, account='{Account}'", userId, found, account);
            return found;
        }

        public void RemoveSelectedAccount(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            _cache.Remove(UserKey(userId));
            _logger.LogInformation("MemoryUserSelectionService: Removed selection for {UserId}", userId);
        }

        private string UserKey(string userId) => $"UserSelection:{userId}";
    }
}
