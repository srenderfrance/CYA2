using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Cya2.Application.Services;

public sealed record ImportPreview(byte[] Data, string FileName, string ContentType, DateTime CreatedAtUtc, string OwnerUserId, string ImportType);

public sealed class ImportPreviewStore
{
    private readonly int _maximumRetainedPreviews;
    private readonly long _maximumRetainedBytes;
    private readonly ConcurrentDictionary<string, ImportPreview> _previews = new(StringComparer.Ordinal);
    private long _retainedBytes;

    public ImportPreviewStore(IOptions<ImportUploadOptions> options)
    {
        var settings = options.Value;
        if (settings.MaximumRetainedPreviews <= 0 || settings.MaximumRetainedPreviewBytes <= 0)
            throw new InvalidOperationException("Preview retention limits must be greater than zero.");

        _maximumRetainedPreviews = settings.MaximumRetainedPreviews;
        _maximumRetainedBytes = settings.MaximumRetainedPreviewBytes;
    }

    public bool TryRemoveOwned(string previewId, string ownerUserId, string importType, out ImportPreview? preview)
    {
        lock (_previews)
        {
            if (!_previews.TryGetValue(previewId, out var candidate) ||
                !string.Equals(candidate.OwnerUserId, ownerUserId, StringComparison.Ordinal) ||
                !string.Equals(candidate.ImportType, importType, StringComparison.OrdinalIgnoreCase))
            {
                preview = null;
                return false;
            }

            if (!_previews.TryRemove(previewId, out preview) || preview is null)
                return false;

            _retainedBytes -= preview.Data.LongLength;
            return true;
        }
    }

    public bool TrySet(string previewId, ImportPreview preview)
    {
        lock (_previews)
        {
            if (_previews.Count >= _maximumRetainedPreviews ||
                _retainedBytes > _maximumRetainedBytes - preview.Data.LongLength)
            {
                return false;
            }

            if (_previews.TryAdd(previewId, preview))
            {
                _retainedBytes += preview.Data.LongLength;
                return true;
            }

            return false;
        }
    }

    public bool TryRemove(string previewId, out ImportPreview? preview)
    {
        lock (_previews)
        {
            if (!_previews.TryRemove(previewId, out preview) || preview is null)
                return false;

            _retainedBytes -= preview.Data.LongLength;
            return true;
        }
    }

    public int RemoveExpired(DateTime utcNow, TimeSpan lifetime)
    {
        var removed = 0;
        foreach (var entry in _previews)
        {
            if (utcNow - entry.Value.CreatedAtUtc < lifetime)
            {
                continue;
            }

            lock (_previews)
            {
                if (_previews.TryRemove(new KeyValuePair<string, ImportPreview>(entry.Key, entry.Value)))
                {
                    _retainedBytes -= entry.Value.Data.LongLength;
                    removed++;
                }
            }
        }

        return removed;
    }
}
