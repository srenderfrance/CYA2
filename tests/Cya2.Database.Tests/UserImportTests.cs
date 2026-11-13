using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using Xunit;

namespace Cya2.Database.Tests;

public sealed class UserImportTests
{
    private readonly Authorization _authorization = new();
    private readonly Users _users = new();
    private readonly Store _store = new();
    private readonly Invalidator _invalidator = new();
    private byte[] _workbookBytes = [];

    public UserImportTests()
    {
        ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");
    }

    [Fact]
    public async Task ValidWorkbookMapsRolesAndImportsExactlyPreviewedRows()
    {
        WriteWorkbook(("first.user@scene8.net", "normal"), ("other.person@scene8.net", "view all"), ("third.person@scene8.net", "admin"));
        var service = CreateService();
        var preview = await PreviewAsync(service);
        Assert.True(preview.CanImport);
        Assert.Equal(new[] { "User", "Viewer", "Admin" }, preview.Rows.Select(row => row.AuthLevel));
        Assert.Equal("First User", preview.Rows[0].Name);
        WriteWorkbook(("changed.person@scene8.net", "admin"));
        var result = await service.ImportAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal("first.user@scene8.net", _store.Entries![0].Email);
        Assert.Equal(3, _store.Entries.Count);
        Assert.Equal(CacheDataDomains.Authorization, _invalidator.Domains);
        Assert.False((await service.ImportAsync()).IsSuccess);
    }

    [Fact]
    public async Task InvalidAndDuplicateRowsPreventEntireImport()
    {
        WriteWorkbook(("first.user@scene8.net", "normal"), ("FIRST.USER@scene8.net", "admin"), ("wrong@example.com", "normal"), ("bad.level@scene8.net", "other"));
        var service = CreateService();
        var preview = await PreviewAsync(service);
        Assert.False(preview.CanImport);
        Assert.Equal(3, preview.Errors.Count);
        Assert.False((await service.ImportAsync()).IsSuccess);
        Assert.Null(_store.Entries);
    }

    [Fact]
    public async Task ExistingUsersAreSkippedAndChangedAdminCannotCommit()
    {
        WriteWorkbook(("first.user@scene8.net", "normal"), ("other.person@scene8.net", "view all"));
        var service = CreateService();
        _users.ExistingEmails.Add("first.user@scene8.net");
        var preview = await PreviewAsync(service);
        Assert.True(preview.CanImport);
        Assert.Equal(1, preview.SkippedCount);
        Assert.True(preview.Rows[0].IsExisting);
        Assert.False(preview.Rows[1].IsExisting);
        var result = await service.ImportAsync();
        Assert.True(result.IsSuccess);
        Assert.Contains("existing users skipped: 1", result.Message);
        Assert.Single(_store.Entries!);
        Assert.Equal("other.person@scene8.net", _store.Entries[0].Email);

        WriteWorkbook(("new.person@scene8.net", "normal"));
        Assert.True((await PreviewAsync(service)).CanImport);
        _authorization.Actor = 2;
        Assert.False((await service.ImportAsync()).IsSuccess);
    }

    private UserImportService CreateService() => new(_authorization, _users, _store, _invalidator, new Guard(), new Audit(), new Preload(), NullLogger<UserImportService>.Instance);

    private async Task<Cya2.Application.DTOs.UserImportPreviewDto> PreviewAsync(UserImportService service)
    {
        await using var stream = new MemoryStream(_workbookBytes);
        return await service.PreviewAsync(stream, "users.xlsx");
    }

    private void WriteWorkbook(params (string Email, string Level)[] rows)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Users");
        sheet.Cells[1, 1].Value = "User";
        sheet.Cells[1, 2].Value = "Level";
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cells[i + 2, 1].Value = rows[i].Email;
            sheet.Cells[i + 2, 2].Value = rows[i].Level;
        }
        _workbookBytes = package.GetAsByteArray();
    }

    private sealed class Authorization : IAdminAuthorizationContext
    {
        public int? Actor { get; set; } = 1;
        public Task<bool> IsCurrentUserAdminAsync() => Task.FromResult(Actor.HasValue);
        public Task<int?> GetCurrentAdminUserIdAsync() => Task.FromResult(Actor);
    }

    private sealed class Users : IUserRepository
    {
        public HashSet<string> ExistingEmails { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<bool> ExistsAsync(string email) => Task.FromResult(ExistingEmails.Contains(email));
        public Task<bool> ExistsAsync(int id) => throw new NotImplementedException();
        public Task<User?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<List<User>> GetAllAsync() => throw new NotImplementedException();
        public Task<User> AddAsync(User entity) => throw new NotImplementedException();
        public Task<User> UpdateAsync(User entity) => throw new NotImplementedException();
        public Task DeleteAsync(int id) => throw new NotImplementedException();
        public Task<User?> GetByEmailAsync(string email) => throw new NotImplementedException();
        public Task<User?> GetByExternalIdAsync(string externalId) => throw new NotImplementedException();
        public Task<List<User>> GetActiveUsersAsync() => throw new NotImplementedException();
    }

    private sealed class Store : IUserBatchAdministrationStore
    {
        public IReadOnlyList<ImportUserEntry>? Entries { get; private set; }
        public Task<UserAdministrationResult> CreateBatchAsync(int actorId, IReadOnlyList<ImportUserEntry> users)
        {
            Entries = users;
            return Task.FromResult(new UserAdministrationResult(UserAdministrationStatus.Success));
        }
    }

    private sealed class Invalidator : IImportCacheInvalidator
    {
        public CacheDataDomains Domains { get; private set; }
        public void InvalidateAll() => Domains = CacheDataDomains.All;
        public void Invalidate(CacheDataDomains domains) => Domains = domains;
    }

    private sealed class Guard : IDatabaseGuard
    {
        public bool IsAvailable => true;
        public void ThrowIfUnavailable() { }
    }

    private sealed class Audit : IAuditEventService
    {
        public Task RecordAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Preload : IAdminPreloadService
    {
        public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Cya2.Application.DTOs.AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Cya2.Application.DTOs.AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Invalidate() { }
    }
}
