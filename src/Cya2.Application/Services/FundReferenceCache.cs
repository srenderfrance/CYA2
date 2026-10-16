using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public sealed class FundReferenceCache : IFundReferenceCache
{
    private readonly object _sync = new();
    private long _generation = -1;
    private IReadOnlyList<FundReferenceDto>? _references;
    private Lazy<Task<IReadOnlyList<FundReferenceDto>>>? _inflight;

    public async Task<IReadOnlyList<FundReferenceDto>> GetOrCreateAsync(
        long generation,
        Func<CancellationToken, Task<IReadOnlyList<FundReferenceDto>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_sync)
        {
            if (_generation == generation && _references is not null)
            {
                return _references;
            }

            if (_generation != generation)
            {
                _generation = generation;
                _references = null;
                _inflight = null;
            }

            _inflight ??= new Lazy<Task<IReadOnlyList<FundReferenceDto>>>(
                () => LoadAsync(generation, factory),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return await _inflight.Value.WaitAsync(cancellationToken);
    }

    public bool TryGet(long generation, out IReadOnlyList<FundReferenceDto> references)
    {
        lock (_sync)
        {
            if (_generation == generation && _references is not null)
            {
                references = _references;
                return true;
            }
        }

        references = Array.Empty<FundReferenceDto>();
        return false;
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _generation = -1;
            _references = null;
            _inflight = null;
        }
    }

    private async Task<IReadOnlyList<FundReferenceDto>> LoadAsync(
        long generation,
        Func<CancellationToken, Task<IReadOnlyList<FundReferenceDto>>> factory)
    {
        var loaded = (await factory(CancellationToken.None)).ToArray();
        lock (_sync)
        {
            if (_generation == generation)
            {
                _references = loaded;
            }

            _inflight = null;
        }

        return loaded;
    }
}