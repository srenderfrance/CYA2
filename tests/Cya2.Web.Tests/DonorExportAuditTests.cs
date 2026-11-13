using System.Text;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using cya2.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class DonorExportAuditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkbookIsReleasedOnlyAfterRequiredAudit(bool rejectAudit)
    {
        var audit = new Audit { Reject = rejectAudit };
        var controller = Controller(new Export(), audit);
        var result = await controller.ExportData();
        Assert.Equal(1, audit.RequiredCalls);
        if (rejectAudit) Assert.IsType<ObjectResult>(result);
        else Assert.NotEmpty(Assert.IsType<FileContentResult>(result).FileContents);
    }

    [Fact]
    public async Task GenerationFailureNeverRecordsCompletion()
    {
        var audit = new Audit();
        var result = await Controller(new Export { Fail = true }, audit).ExportData();
        Assert.IsType<ObjectResult>(result);
        Assert.Equal(0, audit.RequiredCalls);
        Assert.Contains(AuditEventTypes.DonorExportFailed, audit.Events);
    }

    private static DonorExportController Controller(Export export, Audit audit)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"AccountId\":1,\"AllDates\":true}"));
        return new DonorExportController(export, new Resolver(), NullLogger<DonorExportController>.Instance, new Antiforgery(), audit)
        { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private sealed class Export : IDonorExportService
    {
        public bool Fail;
        public Task<DonorExportResult> GetExportDataAsync(string userId, bool hint, IEnumerable<string> funds, bool allDates, DateTime? start, DateTime? end)
            => Fail ? throw new InvalidOperationException("private sentinel") : Task.FromResult(new DonorExportResult(true, true, ["Test"], [new DonorSummaryDto()]));
        public Task<DonorExportResult> GetSelectionExportDataAsync(string userId, bool hint, int accountId, string selection, bool allDates, DateTime? start, DateTime? end)
            => GetExportDataAsync(userId, hint, [], allDates, start, end);
    }
    private sealed class Resolver : IUserIdResolver
    {
        public string ResolveUserId(System.Security.Claims.ClaimsPrincipal user, string? fallbackCurrentUserId = null) => "42";
    }
    private sealed class Audit : IAuditEventService
    {
        public bool Reject;
        public int RequiredCalls;
        public List<string> Events { get; } = [];
        public Task RecordRequiredAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default)
        {
            RequiredCalls++;
            if (Reject) throw new AuditPersistenceException();
            return RecordAsync(actorUserId, eventType, outcome, traceId, itemCount, cancellationToken);
        }
        public Task RecordAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default)
        { Events.Add(eventType); return Task.CompletedTask; }
    }
    private sealed class Antiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => new("request", "cookie", "form", "header");
        public AntiforgeryTokenSet GetTokens(HttpContext context) => GetAndStoreTokens(context);
        public void SetCookieTokenAndHeader(HttpContext context) { }
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;
    }
}
