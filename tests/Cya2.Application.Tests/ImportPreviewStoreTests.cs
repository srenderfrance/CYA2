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
        Assert.True(store.TrySet("old", new ImportPreview(Array.Empty<byte>(), "old.xlsx", "", now.AddMinutes(-16), "1", "donations")));
        Assert.True(store.TrySet("current", new ImportPreview(Array.Empty<byte>(), "current.xlsx", "", now.AddMinutes(-14), "1", "donations")));

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

    [Fact]
    public void RemovesPreviewOnlyForMatchingOwnerAndImportType()
    {
        var store = CreateStore(maximumPreviews: 2, maximumBytes: 100);
        Assert.True(store.TrySet("preview", new ImportPreview(new byte[10], "test.xlsx", "", DateTime.UtcNow, "owner-1", "donations")));

        Assert.False(store.TryRemoveOwned("preview", "owner-2", "donations", out _));
        Assert.False(store.TryRemoveOwned("preview", "owner-1", "accounting", out _));
        Assert.True(store.TryRemoveOwned("preview", "owner-1", "donations", out var removed));
        Assert.NotNull(removed);
    }

    [Fact]
    public void OwnedRemovalIsSingleUse()
    {
        var store = CreateStore(maximumPreviews: 2, maximumBytes: 100);
        Assert.True(store.TrySet("preview", new ImportPreview(new byte[10], "test.xlsx", "", DateTime.UtcNow, "owner-1", "donations")));

        Assert.True(store.TryRemoveOwned("preview", "owner-1", "donations", out _));
        Assert.False(store.TryRemoveOwned("preview", "owner-1", "donations", out _));
    }

    [Fact]
    public void ExpirationRemovesOwnedPreviewAndReleasesCapacity()
    {
        var store = CreateStore(maximumPreviews: 1, maximumBytes: 20);
        var created = DateTime.UtcNow.AddMinutes(-16);
        Assert.True(store.TrySet("expired", new ImportPreview(new byte[20], "test.xlsx", "", created, "owner-1", "donations")));

        Assert.Equal(1, store.RemoveExpired(DateTime.UtcNow, TimeSpan.FromMinutes(15)));
        Assert.True(store.TrySet("replacement", new ImportPreview(new byte[20], "test.xlsx", "", DateTime.UtcNow, "owner-2", "donations")));
    }

    private static ImportPreviewStore CreateStore(int maximumPreviews = 2, long maximumBytes = 100)
        => new(Options.Create(new ImportUploadOptions
        {
            MaximumRetainedPreviews = maximumPreviews,
            MaximumRetainedPreviewBytes = maximumBytes
        }));

    private static ImportPreview CreatePreview(int size)
        => new(new byte[size], "test.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DateTime.UtcNow, "owner", "donations");
}
