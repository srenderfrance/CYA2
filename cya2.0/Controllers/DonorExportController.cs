using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cya2.Core.ValueObjects;
using Cya2.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Antiforgery;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/donors")]
    [Authorize]
    public class DonorExportController : ControllerBase
    {
        private readonly IDonorExportService _donorExportService;
        private readonly IUserIdResolver _userIdResolver;
        private readonly ILogger<DonorExportController> _logger;
        private readonly IAntiforgery _antiforgery;

        public DonorExportController(
            IDonorExportService donorExportService,
            IUserIdResolver userIdResolver,
            ILogger<DonorExportController> logger,
            IAntiforgery antiforgery)
        {
            _donorExportService = donorExportService;
            _userIdResolver = userIdResolver;
            _logger = logger;
            _antiforgery = antiforgery;
        }

        public class ExportRequest
        {
            public List<string> Funds { get; set; } = new List<string>();
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public bool AllDates { get; set; }
            public bool IncludeTotal { get; set; } = true;
            public bool IncludeEmail { get; set; } = false;
            public bool IncludePhone { get; set; } = false;
            public bool IncludeAddress { get; set; } = false;
            public bool IncludePaymentMethod { get; set; } = false;
            public bool IncludeFrequency { get; set; } = true;
        }

        [HttpPost("export-data")]
        public async Task<IActionResult> ExportData()
        {
            try
            {
                try
                {
                    await _antiforgery.ValidateRequestAsync(HttpContext);
                }
                catch (AntiforgeryValidationException ex)
                {
                    var hasHeaderToken = Request.Headers.ContainsKey("RequestVerificationToken");
                    var hasFormToken = Request.HasFormContentType && Request.Form.ContainsKey("__RequestVerificationToken");
                    var antiforgeryCookies = Request.Cookies.Keys
                        .Where(k => k.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    _logger.LogWarning(ex,
                        "Antiforgery validation failed for {Path}. HasHeaderToken={HasHeaderToken}, HasFormToken={HasFormToken}, HeaderNames={HeaderNames}, AntiforgeryCookies={AntiforgeryCookies}, ContentType={ContentType}",
                        Request.Path,
                        hasHeaderToken,
                        hasFormToken,
                        string.Join(",", Request.Headers.Keys),
                        string.Join(",", antiforgeryCookies),
                        Request.ContentType ?? string.Empty);

                    return BadRequest("Antiforgery validation failed.");
                }

                // Support both form-submitted JSON (Request.Form["request"]) and raw JSON body
                ExportRequest? req = null;

                if (Request.HasFormContentType && Request.Form.ContainsKey("request"))
                {
                    var json = Request.Form["request"].ToString();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            req = JsonSerializer.Deserialize<ExportRequest>(json);
                        }
                        catch (Exception) { /* ignore */ }
                    }
                }
                else
                {
                    // Try to read raw JSON body
                    using var sr = new System.IO.StreamReader(Request.Body);
                    var body = await sr.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            req = JsonSerializer.Deserialize<ExportRequest>(body);
                        }
                        catch (Exception) { /* ignore */ }
                    }
                }

                if (req == null)
                {
                    return BadRequest("No export data provided.");
                }

                if (req.Funds == null || !req.Funds.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    return BadRequest("At least one fund is required.");
                }

                var userId = _userIdResolver.ResolveUserId(User);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized();
                }

                var authLevel = User.FindFirst("AuthLevel")?.Value ?? string.Empty;
                var isAdminOrViewerHint = User.IsInRole("Admin")
                    || string.Equals(authLevel, "Admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(authLevel, "Viewer", StringComparison.OrdinalIgnoreCase);

                var exportData = await _donorExportService.GetExportDataAsync(
                    userId,
                    isAdminOrViewerHint,
                    req.Funds,
                    req.AllDates,
                    req.StartDate,
                    req.EndDate);
                if (!exportData.UserContextFound)
                {
                    return Forbid();
                }
                if (!exportData.FundsAuthorized)
                {
                    _logger.LogWarning("ExportData denied. TraceId={TraceId}, UserId={UserId}, RequestedFundCount={RequestedFundCount}",
                        HttpContext.TraceIdentifier,
                        userId,
                        exportData.RequestedFunds.Count);
                    return Forbid();
                }

                var requestedFunds = exportData.RequestedFunds.ToList();
                var donorRows = exportData.Donors;

                if (donorRows == null || donorRows.Count == 0)
                {
                    return BadRequest("No export data provided.");
                }

                _logger.LogInformation("DonorExportController: ExportData request for funds={Funds}; generatedRows={Count}",
                    string.Join(",", requestedFunds), donorRows.Count);

                // Use EPPlus license API for non-commercial organization
                ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");
                using var package = new ExcelPackage();
                var ws = package.Workbook.Worksheets.Add("Donors");
                var licenseComment = "This workbook has been created with EPPlus licensed to Servant Partners under The Polyform Noncommercial License: See https://polyformproject.org/license";
                ws.Cells[1, 1].Value = ws.Cells[1, 1].Value ?? string.Empty;
                var cmt2 = ws.Cells[1, 1].AddComment(licenseComment, "Servant Partners");
                cmt2.AutoFit = true;
                cmt2.Visible = false;

                // Build header row dynamically
                var col = 1;
                ws.Cells[1, col++].Value = "Name";
                if (req.IncludeTotal) ws.Cells[1, col++].Value = "Total";
                if (req.IncludePaymentMethod) ws.Cells[1, col++].Value = "PaymentMethod";
                if (req.IncludeEmail) ws.Cells[1, col++].Value = "Email";
                if (req.IncludePhone) ws.Cells[1, col++].Value = "Phone";
                if (req.IncludeAddress) ws.Cells[1, col++].Value = "Address";
                if (req.IncludeFrequency) ws.Cells[1, col++].Value = "Frequency";

                var rowIndex = 2;
                foreach (var r in donorRows)
                {
                    col = 1;
                    ws.Cells[rowIndex, col++].Value = r.Name;
                    if (req.IncludeTotal) { ws.Cells[rowIndex, col].Value = r.Total; ws.Cells[rowIndex, col++].Style.Numberformat.Format = "#,##0.00"; }
                    if (req.IncludePaymentMethod) ws.Cells[rowIndex, col++].Value = r.PaymentMethod;
                    if (req.IncludeEmail) ws.Cells[rowIndex, col++].Value = r.Email;
                    if (req.IncludePhone) ws.Cells[rowIndex, col++].Value = r.PhoneSummary;
                    if (req.IncludeAddress) ws.Cells[rowIndex, col++].Value = r.AddressSummary;
                    if (req.IncludeFrequency) ws.Cells[rowIndex, col++].Value = GetFrequencyLabel(r.Frequency);
                    rowIndex++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                var bytes = package.GetAsByteArray();
                var fileName = BuildExportFileName(requestedFunds, req.AllDates, req.StartDate, req.EndDate);
                // Return the file as a FileContentResult
                var result = File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Donor export failed. TraceId={TraceId}", HttpContext.TraceIdentifier);
                return Problem(
                    detail: $"An unexpected error occurred while generating the export. TraceId={HttpContext.TraceIdentifier}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        private static string BuildExportFileName(List<string> requestedFunds, bool allDates, DateTime? startDate, DateTime? endDate)
        {
            var fundPart = requestedFunds.Count == 1
                ? SanitizeFilePart(requestedFunds[0])
                : $"multi_{requestedFunds.Count}_funds";

            var rangePart = allDates
                ? "all-dates"
                : $"{(startDate ?? DateTime.MinValue):yyyyMMdd}-{(endDate ?? DateTime.MaxValue):yyyyMMdd}";

            return $"donors_{fundPart}_{rangePart}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "all";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            cleaned = cleaned.Replace(' ', '_');
            return string.IsNullOrWhiteSpace(cleaned) ? "all" : cleaned;
        }

        private static string GetFrequencyLabel(Cya2.Core.Enums.DonorFrequency frequency) => frequency switch
        {
            Cya2.Core.Enums.DonorFrequency.OneTime => "One-time",
            Cya2.Core.Enums.DonorFrequency.Monthly => "Monthly",
        Cya2.Core.Enums.DonorFrequency.Quarterly => "Quarterly",
            Cya2.Core.Enums.DonorFrequency.Yearly => "Yearly",
            Cya2.Core.Enums.DonorFrequency.Sporadic => "Sporadic",
            _ => string.Empty
        };
    }
}
