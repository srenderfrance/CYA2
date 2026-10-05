using Cya2.Application.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ImportPreviewStoreTests
{
    [Fact]
    public void RemoveExpiredRemovesOnlyExpiredPreviews()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        Assert.True(store.TrySet("old", new ImportPreview(Array.Empty<byte>(), "old.xlsx", "", now.AddMinutes(-16))));
        Assert.True(store.TrySet("current", new ImportPreview(Array.Empty<byte>(), "current.xlsx", "", now.AddMinutes(-14))));

        var removed = store.RemoveExpired(now, TimeSpan.FromMinutes(15));

        Assert.Equal(1, removed);
        Assert.False(store.TryRemove("old", out _));
        Assert.True(store.TryRemove("current", out _));
    }

    [Fact]
    public void RejectsPreviewWhenCountLimitIsReached()
    {
        var store = CreateStore(maximumPreviews: 2, maximumBytes: 100);

        Assert.True(store.TrySet("one", CreatePreview(40)));
        Assert.True(store.TrySet("two", CreatePreview(40)));
        Assert.False(store.TrySet("three", CreatePreview(1)));
    }

    [Fact]
    public void RejectsPreviewWhenAggregateByteLimitIsReached()
    {
        var store = CreateStore(maximumPreviews: 3, maximumBytes: 100);

        Assert.True(store.TrySet("one", CreatePreview(60)));
        Assert.False(store.TrySet("two", CreatePreview(41)));
        Assert.True(store.TrySet("two", CreatePreview(40)));
    }

    private static ImportPreviewStore CreateStore(int maximumPreviews = 2, long maximumBytes = 100)
        => new(Options.Create(new ImportUploadOptions
        {
            MaximumRetainedPreviews = maximumPreviews,
            MaximumRetainedPreviewBytes = maximumBytes
        }));

    private static ImportPreview CreatePreview(int size)
        => new(new byte[size], "test.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DateTime.UtcNow);
}
