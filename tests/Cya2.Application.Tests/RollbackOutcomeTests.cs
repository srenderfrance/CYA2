using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class RollbackOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialRollbackInvalidatesEvenWhenAccountingFails(bool throws)
    {
        var executor = new Executor { AccountingThrows = throws };
        var cache = new Invalidator();
        var result = await Service(executor, cache).ExecuteRollbackAsync("both");
        Assert.False(result.Success);
        Assert.True(result.ChangesCommitted);
        Assert.Equal(0, result.DonationRowsRestored);
        Assert.True(cache.Called);
    }

    [Fact]
    public async Task UnknownDonationOutcomeInvalidatesAndStopsCombinedRollback()
    {
        var executor = new Executor { Donation = new() { OutcomeUnknown = true, ErrorMessage = "Reconcile before retrying." } };
        var cache = new Invalidator();
        var result = await Service(executor, cache).ExecuteRollbackAsync("both");
        Assert.True(result.OutcomeUnknown);
        Assert.False(result.Success);
        Assert.True(cache.Called);
        Assert.False(executor.AccountingCalled);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NotificationFailurePreservesCommittedRollback(bool cacheFailure, bool auditFailure)
    {
        var result = await Service(new Executor(), new Invalidator { Throw = cacheFailure }, new Audit { Throw = auditFailure })
            .ExecuteRollbackAsync("donations");
        Assert.True(result.Success);
        Assert.True(result.ChangesCommitted);
        Assert.NotEmpty(result.Warnings);
    }

    private static RollbackOrchestrationService Service(Executor executor, Invalidator cache, Audit? audit = null) =>
        new(new Repository(), executor, cache, new Admin(), audit ?? new Audit(), NullLogger<RollbackOrchestrationService>.Instance);

    private sealed class Executor : IRollbackExecutor
    {
        public RollbackResult Donation = new() { Success = true, ChangesCommitted = true };
        public bool AccountingThrows;
        public bool AccountingCalled;
        public Task<RollbackResult> RollbackDonationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Donation);
        public Task<RollbackResult> RollbackAccountingAsync(CancellationToken cancellationToken = default)
        {
            AccountingCalled = true;
            if (AccountingThrows) throw new InvalidOperationException("Injected failure");
            return Task.FromResult(new RollbackResult { ErrorMessage = "No accounting snapshot." });
        }
    }
    private sealed class Invalidator : IImportCacheInvalidator
    {
        public bool Called;
        public bool Throw;
        public void InvalidateAll()
        {
            Called = true;
            if (Throw) throw new InvalidOperationException("Injected cache failure");
        }
        public void Invalidate(CacheDataDomains domains) => InvalidateAll();
    }
    private sealed class Admin : IAdminAuthorizationContext
    {
        public Task<bool> IsCurrentUserAdminAsync() => Task.FromResult(true);
        public Task<int?> GetCurrentAdminUserIdAsync() => Task.FromResult<int?>(1);
    }
    private sealed class Audit : IAuditEventService
    {
        public bool Throw;
        public Task RecordAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default)
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            if (Throw) throw new InvalidOperationException("Injected audit failure");
            return Task.CompletedTask;
        }
    }
    private sealed class Repository : IRollbackRepository
    {
        public Task<IReadOnlyList<BackupSummary>> GetAvailableDonationBackupsAsync(int limit = 5, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BackupSummary>>([]);
        public Task<IReadOnlyList<BackupSummary>> GetAvailableAccountingBackupsAsync(int limit = 5, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BackupSummary>>([]);
    }
}
