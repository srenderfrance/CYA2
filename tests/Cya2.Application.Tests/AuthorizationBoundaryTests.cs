using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class AuthorizationBoundaryTests
{
    [Fact]
    public async Task ImportFromPreview_RejectsDifferentOwnerWithoutCallingProcessor()
    {
        var previews = CreatePreviewStore();
        Assert.True(previews.TrySet("preview-1", new ImportPreview([1, 2, 3], "test.xlsx", "", DateTime.UtcNow, "owner-1", "donations")));
        var processor = new RecordingImportProcessor("donations");
        var service = CreateImportService(previews, processor, new ImportActor("owner-2", true));

        var result = await service.ImportFromPreviewAsync("preview-1", "donations");

        Assert.Contains(result.Errors, error => error.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, processor.CallCount);
        Assert.True(previews.TryRemoveOwned("preview-1", "owner-1", "donations", out _));
    }

    [Fact]
    public async Task ImportFromPreview_AllowsOwnerAndRecordsOwnerOnProgress()
    {
        var previews = CreatePreviewStore();
        Assert.True(previews.TrySet("preview-1", new ImportPreview([1, 2, 3], "test.xlsx", "", DateTime.UtcNow, "owner-1", "donations")));
        var processor = new RecordingImportProcessor("donations");
        var progress = new RecordingProgressService();
        var service = CreateImportService(previews, processor, new ImportActor("owner-1", true), progress);

        var result = await service.ImportFromPreviewAsync("preview-1", "donations");

        Assert.Equal(1, processor.CallCount);
        Assert.NotNull(result);
        Assert.Single(progress.Started);
        Assert.Equal("owner-1", progress.Started[0].OwnerUserId);
    }

    [Fact]
    public async Task DonorExport_RejectsUnauthorizedFundWithoutReadingDonors()
    {
        var donorService = new RecordingDonorService();
        var authorization = CreateAuthorizationService(new User { Id = 7, Email = "user@example.test", AuthLevel = "User" }, new Dictionary<int, HashSet<int>> { [7] = [12] });
        var context = new FakeUserAccountContextService(new UserAccountContext
        {
            UserId = 7,
            Accounts = [new UserAccountContextAccount { AccountId = 12, Fund = "Allowed" }]
        });
        var service = new DonorExportService(donorService, context, authorization);

        var result = await service.GetExportDataAsync("7", false, ["Forbidden"], true, null, null);

        Assert.False(result.FundsAuthorized);
        Assert.Equal(0, donorService.ReadCount);
    }

    [Fact]
    public async Task DonorExport_AllowsOnlyCurrentAccountMembership()
    {
        var donorService = new RecordingDonorService();
        var authorization = CreateAuthorizationService(new User { Id = 7, Email = "user@example.test", AuthLevel = "User" }, new Dictionary<int, HashSet<int>> { [7] = [12] });
        var context = new FakeUserAccountContextService(new UserAccountContext
        {
            UserId = 7,
            Accounts = [new UserAccountContextAccount { AccountId = 12, Fund = "Allowed" }]
        });
        var service = new DonorExportService(donorService, context, authorization);

        var result = await service.GetExportDataAsync("7", false, ["Allowed"], true, null, null);

        Assert.True(result.FundsAuthorized);
        Assert.Equal(1, donorService.ReadCount);
    }

    [Fact]
    public async Task DonorExport_RejectsDeletedUserBeforeReadingDonors()
    {
        var donorService = new RecordingDonorService();
        var authorization = CreateAuthorizationService(null, new Dictionary<int, HashSet<int>>());
        var service = new DonorExportService(
            donorService,
            new FakeUserAccountContextService(null),
            authorization);

        var result = await service.GetExportDataAsync("deleted@example.test", false, ["Allowed"], true, null, null);

        Assert.False(result.FundsAuthorized);
        Assert.Equal(0, donorService.ReadCount);
    }

    private static ImportOrchestrationService CreateImportService(
        ImportPreviewStore previews,
        RecordingImportProcessor processor,
        ImportActor actor,
        RecordingProgressService? progress = null)
        => new(
            [processor],
            progress ?? new RecordingProgressService(),
            new RecordingWorkQueue(),
            previews,
            new ImportUploadValidator(Options.Create(new ImportUploadOptions())),
            new FakeImportAuthorizationContext(actor),
            NullLogger<ImportOrchestrationService>.Instance);

    private static ImportPreviewStore CreatePreviewStore()
        => new(Options.Create(new ImportUploadOptions
        {
            MaximumRetainedPreviews = 5,
            MaximumRetainedPreviewBytes = 100
        }));

    private static UserAuthorizationService CreateAuthorizationService(User? user, Dictionary<int, HashSet<int>> memberships)
        => new(
            new AuthorizationUserRepository(user),
            new AuthorizationAccountRepository(memberships),
            NullLogger<UserAuthorizationService>.Instance);

    private sealed class FakeImportAuthorizationContext(ImportActor actor) : IImportAuthorizationContext
    {
        public Task<ImportActor?> GetCurrentActorAsync() => Task.FromResult<ImportActor?>(actor);
    }

    private sealed class RecordingImportProcessor(string importType) : IImportProcessor
    {
        public string ImportType => importType;
        public int CallCount { get; private set; }

        public Task<ImportResult> ProcessAsync(Stream file, string progressId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ImportResult { ProgressId = progressId });
        }
    }

    private sealed class RecordingProgressService : IImportProgressService
    {
        public List<(string ProgressId, string OwnerUserId)> Started { get; } = [];
        public void Start(string id, string importType, string ownerUserId) => Started.Add((id, ownerUserId));
        public void AddStep(string id, string stepName, string status = "Starting...") { }
        public void UpdateStep(string id, string stepName, string status, string? details = null) { }
        public void CompleteStep(string id, string stepName, string completionStatus, string? details = null) { }
        public void Report(string id, int totalRows, int insertedRows, int failedRows, string? status = null) { }
        public void SetExpected(string id, int expectedRows) { }
        public void SetStatus(string id, string status) { }
        public void AddErrors(string id, IEnumerable<string> errors) { }
        public void Complete(string id) { }
    }

    private sealed class RecordingWorkQueue : IImportWorkQueue
    {
        public ValueTask EnqueueAsync(ImportWorkItem workItem, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeUserAccountContextService(UserAccountContext? context) : IUserAccountContextService
    {
        public Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false) => Task.FromResult(context);
        public UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredFund) => context.Accounts.FirstOrDefault();
        public void Invalidate(string userId) { }
    }

    private sealed class RecordingDonorService : IDonorService
    {
        public int ReadCount { get; private set; }
        public string? GetLastQuery() => null;
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(IEnumerable<string> fundNames) { ReadCount++; return Task.FromResult(new List<DonorSummaryDto>()); }
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(IEnumerable<string> fundNames, DateRange dateRange) { ReadCount++; return Task.FromResult(new List<DonorSummaryDto>()); }
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(int accountId, string accountFund, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesForSelectionAsync(AccountOptionDto account, string selectedSubAccount, DateRange? dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetMissingGiftDonorsAsync(AccountOptionDto account, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(int accountId, string accountFund) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund) => Task.FromResult<DonorDetailDto?>(null);
        public Task<DonorDetailDto?> GetDonorDetailForAccountAsync(string donorName, AccountOptionDto account) => Task.FromResult<DonorDetailDto?>(null);
        public Task<List<string>> GetDonorNamesAsync(string accountFund) => Task.FromResult(new List<string>());
        public Task<List<SubAccount>> GetSubAccountsForAccountAsync(int accountId) => Task.FromResult(new List<SubAccount>());
        public Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund) => Task.FromResult(string.Empty);
        public Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund) => Task.FromResult(new List<DonorSummaryDto>());
        public Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, string address, string city, string state, string postal, string country) => Task.CompletedTask;
    }

    private sealed class AuthorizationUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(int id) => Task.FromResult(user?.Id == id ? user : null);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(user?.Email?.Equals(email, StringComparison.OrdinalIgnoreCase) == true ? user : null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(user is null ? [] : new List<User> { user });
        public Task<List<User>> GetAllAsync() => GetActiveUsersAsync();
        public Task<User> AddAsync(User entity) => Task.FromResult(entity);
        public Task<User> UpdateAsync(User entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(user?.Id == id);
        public Task<bool> ExistsAsync(string email) => Task.FromResult(user?.Email?.Equals(email, StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed class AuthorizationAccountRepository(Dictionary<int, HashSet<int>> memberships) : IUserAccountAccessRepository
    {
        public Task<bool> HasAccessAsync(int userId, int accountId) => Task.FromResult(memberships.TryGetValue(userId, out var accounts) && accounts.Contains(accountId));
        public Task<List<Account>> GetUserAccountsAsync(int userId) => Task.FromResult(memberships.TryGetValue(userId, out var ids) ? ids.Select(id => new Account { AccountId = id, Fund = id == 12 ? "Allowed" : "Other" }).ToList() : []);
        public Task<Account?> GetAccountByIdAsync(int accountId) => Task.FromResult<Account?>(null);
        public Task<bool> GrantAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAllAccessAsync(int userId) => Task.FromResult(true);
        public Task<int> GetUserAccountCountAsync(int userId) => Task.FromResult(0);
        public Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId) => Task.FromResult(true);
    }
}
