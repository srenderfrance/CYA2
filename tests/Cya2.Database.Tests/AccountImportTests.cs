using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OfficeOpenXml;
using Xunit;

namespace Cya2.Database.Tests;

public sealed class AccountImportTests(MySqlFixture mysql) : IDisposable, IClassFixture<MySqlFixture>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cya2-account-import-test-" + Guid.NewGuid().ToString("N"));
    private readonly Accounts _accounts = new();
    private readonly Authorization _authorization = new();

    static AccountImportTests()
    {
        ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");
    }

    [Fact]
    public async Task RequestedHeadersDistinguishFundFromFundNumber()
    {
        WriteWorkbook(false);
        var preview = await CreateService().PreviewAsync();
        Assert.True(preview.CanImport, string.Join(" ", preview.Errors));
        Assert.Equal("Primary", preview.Rows[0].Fund);
        Assert.Equal("0012", preview.Rows[0].AccountNumber);
        Assert.Equal("Class A", preview.Rows[0].AccountingClass);
        Assert.Equal(12m, preview.Rows[0].Overhead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrBlankClassAllowsMultipleAccounts(bool includeClass)
    {
        WriteWorkbook(false, includeClass, blankClasses: true);
        _accounts.Items.Add(new Account { Fund = "Other", AccountNumber = "9", AccountingClass = string.Empty });
        var preview = await CreateService().PreviewAsync();
        Assert.True(preview.CanImport, string.Join(" ", preview.Errors));
        Assert.Equal(2, preview.Rows.Count);
        Assert.All(preview.Rows, row => Assert.Equal(string.Empty, row.AccountingClass));
    }

    [Fact]
    public async Task DuplicateClassHeadersAreRejected()
    {
        WriteWorkbook(false);
        var path = Path.Combine(_root, "AccountImports", "input.xlsx");
        using (var package = new ExcelPackage(new FileInfo(path)))
        {
            package.Workbook.Worksheets[0].Cells[1, 5].Value = "Accounting Class";
            package.Save();
        }
        var preview = await CreateService().PreviewAsync();
        Assert.False(preview.CanImport);
        Assert.Contains(preview.Errors, error => error.Contains("Class column appears more than once"));
    }

    [MySqlFact]
    public async Task BulkImportPersistsMultipleBlankClassesAsNull()
    {
        var db = await mysql.CreateDatabaseAsync();
        await MySqlFixture.ExecuteAsync(db.SetupConnection, "INSERT INTO Users (Id, Email, Name, AuthLevel) VALUES (1, 'admin@scene8.net', 'Admin', 'Admin')");
        WriteWorkbook(false, includeClass: true, blankClasses: true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:default"] = db.RuntimeConnection }).Build();
        var service = new AccountImportService(new EnvironmentStub { ContentRootPath = _root }, config, new Cya2.Infrastructure.Repositories.AccountRepository(config, new Guard()), new Invalidator(), new Preload(), new Guard(), _authorization);
        var preview = await service.PreviewAsync();
        Assert.True(preview.CanImport, string.Join(" ", preview.Errors));
        var result = await service.ImportAsync();
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("2", await MySqlFixture.ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts WHERE AccountingClass IS NULL"));
        Assert.Equal("2", await MySqlFixture.ScalarAsync(db.RuntimeConnection, "SELECT COUNT(*) FROM Accounts WHERE SoftCredit='' AND BalanceAdjustment=0"));
    }

    [Fact]
    public async Task DuplicateClassesAndExistingClassesRejectPreview()
    {
        WriteWorkbook(true);
        _accounts.Items.Add(new Account { Fund = "Other", AccountNumber = "9", AccountingClass = "class a" });
        var preview = await CreateService().PreviewAsync();
        Assert.False(preview.CanImport);
        Assert.Contains(preview.Errors, error => error.Contains("Duplicate Class"));
        Assert.Contains(preview.Errors, error => error.Contains("Row 2") && error.Contains("Class"));
    }

    [Fact]
    public async Task UnauthorizedActorCannotPreviewOrCommit()
    {
        WriteWorkbook(false);
        _authorization.Actor = null;
        var service = CreateService();
        Assert.False((await service.PreviewAsync()).CanImport);
        Assert.False((await service.ImportAsync()).IsSuccess);
    }

    private AccountImportService CreateService() => new(new EnvironmentStub { ContentRootPath = _root }, new ConfigurationBuilder().Build(), _accounts, new Invalidator(), new Preload(), new Guard(), _authorization);

    private void WriteWorkbook(bool duplicate, bool includeClass = true, bool blankClasses = false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "AccountImports"));
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Accounts");
        sheet.Cells[1, 1].Value = "fund";
        sheet.Cells[1, 2].Value = "overhead";
        sheet.Cells[1, 3].Value = "fund#";
        if (includeClass) sheet.Cells[1, 4].Value = "class";
        sheet.Cells[2, 1].Value = "Primary";
        sheet.Cells[2, 2].Value = 12;
        sheet.Cells[2, 3].Value = "0012";
        if (!blankClasses) sheet.Cells[2, 4].Value = "Class A";
        if (duplicate || blankClasses)
        {
            sheet.Cells[3, 1].Value = "Another";
            sheet.Cells[3, 2].Value = 10;
            sheet.Cells[3, 3].Value = "0013";
            if (!blankClasses) sheet.Cells[3, 4].Value = "Class A";
        }
        File.WriteAllBytes(Path.Combine(_root, "AccountImports", "input.xlsx"), package.GetAsByteArray());
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Authorization : IAdminAuthorizationContext
    {
        public int? Actor { get; set; } = 1;
        public Task<bool> IsCurrentUserAdminAsync() => Task.FromResult(Actor.HasValue);
        public Task<int?> GetCurrentAdminUserIdAsync() => Task.FromResult(Actor);
    }
    private sealed class Accounts : IAccountRepository
    {
        public List<Account> Items { get; } = [];
        public Task<List<Account>> GetAllAsync() => Task.FromResult(Items);
        public Task<Account?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<Account> AddAsync(Account entity) => throw new NotImplementedException();
        public Task<Account> UpdateAsync(Account entity) => throw new NotImplementedException();
        public Task DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(int id) => throw new NotImplementedException();
        public Task<Account?> GetByFundCodeAsync(string fundCode) => throw new NotImplementedException();
        public Task<Account?> GetByFundAsync(string fund) => throw new NotImplementedException();
        public Task<Account?> GetByAccountNumberAsync(string accountNumber) => throw new NotImplementedException();
        public Task<List<Account>> GetByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<bool> ValidateUserAccessAsync(string userId, string fund) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string fundCode) => throw new NotImplementedException();
    }
    private sealed class Invalidator : IImportCacheInvalidator
    {
        public void InvalidateAll() { }
        public void Invalidate(CacheDataDomains domains) { }
    }
    private sealed class Guard : IDatabaseGuard
    {
        public bool IsAvailable => true;
        public void ThrowIfUnavailable() { }
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
