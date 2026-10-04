using System.Collections.Concurrent;

namespace Cya2.Application.Services;

public sealed record ImportPreview(byte[] Data, string FileName, string ContentType, DateTime CreatedAtUtc);

public sealed class ImportPreviewStore
{
    private readonly ConcurrentDictionary<string, ImportPreview> _previews = new(StringComparer.Ordinal);

    public void Set(string previewId, ImportPreview preview) => _previews[previewId] = preview;

    public bool TryRemove(string previewId, out ImportPreview? preview)
        => _previews.TryRemove(previewId, out preview);

    public int RemoveExpired(DateTime utcNow, TimeSpan lifetime)
    {
        var removed = 0;
        foreach (var entry in _previews)
        {
            if (utcNow - entry.Value.CreatedAtUtc < lifetime)
            {
                continue;
            }

            if (_previews.TryRemove(new KeyValuePair<string, ImportPreview>(entry.Key, entry.Value)))
            {
                removed++;
            }
        }

        return removed;
    }
}
