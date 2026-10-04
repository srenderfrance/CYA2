using Cya2.Application.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ImportPreviewStoreTests
{
    [Fact]
    public void RemoveExpiredRemovesOnlyExpiredPreviews()
    {
        var store = new ImportPreviewStore();
        var now = DateTime.UtcNow;
        store.Set("old", new ImportPreview(Array.Empty<byte>(), "old.xlsx", "", now.AddMinutes(-16)));
        store.Set("current", new ImportPreview(Array.Empty<byte>(), "current.xlsx", "", now.AddMinutes(-14)));

        var removed = store.RemoveExpired(now, TimeSpan.FromMinutes(15));

        Assert.Equal(1, removed);
        Assert.False(store.TryRemove("old", out _));
        Assert.True(store.TryRemove("current", out _));
    }
}
