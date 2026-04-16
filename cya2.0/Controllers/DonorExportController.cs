using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/donors")]
    public class DonorExportController : ControllerBase
    {
        private readonly IDonationReadRepository _donationRepo;
        private readonly ILogger<DonorExportController> _logger;

        public DonorExportController(IDonationReadRepository donationRepo, ILogger<DonorExportController> logger)
        {
            _donationRepo = donationRepo;
            _logger = logger;
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] string fund, [FromQuery] string start, [FromQuery] string end)
        {
            try
            {
                if (string.IsNullOrEmpty(fund)) return BadRequest("Missing fund");

                // Parse dates (expecting ISO or yyyy-MM-dd etc.)
                DateTime startDate;
                DateTime endDate;
                if (!DateTime.TryParse(start, null, DateTimeStyles.RoundtripKind, out startDate))
                {
                    if (!DateTime.TryParse(start, out startDate)) startDate = DateTime.MinValue;
                }
                if (!DateTime.TryParse(end, null, DateTimeStyles.RoundtripKind, out endDate))
                {
                    if (!DateTime.TryParse(end, out endDate)) endDate = DateTime.MaxValue;
                }

                // Log exact fund string being used for SQL
                try { _logger?.LogInformation("DonorExportController: Export requested for fund='{Fund}' start={Start} end={End}", fund, startDate, endDate); } catch { }
                try { Console.WriteLine($"DonorExportController.TRACE: Export called with fund='{fund}' start='{startDate:o}' end='{endDate:o}'"); } catch { }

                // Query donations for the fund and date range
                var rows = await _donationRepo.GetDonationsByFundsAndDateRangeAsync(
                    new[] { fund }, startDate, endDate);

                // Group by donor name
                var grouped = rows
                    .Where(d => !string.IsNullOrWhiteSpace(d.AccountName))
                    .GroupBy(d => d.AccountName!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Total = g.Sum(x => Convert.ToDecimal(x.Amount)),
                        LastDonation = g.Max(x => x.Date),
                        Email = g.Select(x => x.Email).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)) ?? string.Empty,
                        Phone = string.Join("; ", g.Select(x => !string.IsNullOrWhiteSpace(x.PhoneMobile) ? x.PhoneMobile : x.PhoneFixed).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                        Address = g.Select(x => x.Address).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? string.Empty
                    })
                    .OrderByDescending(d => d.Total)
                    .ThenBy(d => d.Name)
                    .ToList();

                // Use EPPlus license API for non-commercial organization
                ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");
                using var package = new ExcelPackage();
                var ws = package.Workbook.Worksheets.Add("Donors");
                // Add license/comment note as a worksheet comment on the header cell
                var licenseComment = "This workbook has been created with EPPlus licensed to Servant Partners under The Polyform Noncommercial License: See https://polyformproject.org/license";
                // Ensure the header cell exists before adding comment
                ws.Cells[1, 1].Value = ws.Cells[1, 1].Value ?? string.Empty;
                var cmt = ws.Cells[1, 1].AddComment(licenseComment, "Servant Partners");
                cmt.AutoFit = true;
                cmt.Visible = false;

                // Headers
                ws.Cells[1, 1].Value = "Name";
                ws.Cells[1, 2].Value = "Total";
                ws.Cells[1, 3].Value = "LastDonation";
                ws.Cells[1, 4].Value = "Email";
                ws.Cells[1, 5].Value = "Phone";
                ws.Cells[1, 6].Value = "Address";

                var row = 2;
                foreach (var d in grouped)
                {
                    ws.Cells[row, 1].Value = d.Name;
                    ws.Cells[row, 2].Value = d.Total;
                    ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                    ws.Cells[row, 3].Value = d.LastDonation;
                    ws.Cells[row, 3].Style.Numberformat.Format = "mm/dd/yyyy";
                    ws.Cells[row, 4].Value = d.Email;
                    ws.Cells[row, 5].Value = d.Phone;
                    ws.Cells[row, 6].Value = d.Address;
                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                var bytes = package.GetAsByteArray();
                var fileName = $"donors_{fund}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // Accept pre-built donor summary data from the client and export the provided rows.
        public class ExportRow
        {
            public string Name { get; set; } = string.Empty;
            public decimal Total { get; set; }
            public DateTime? LastDonation { get; set; }
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string PaymentMethod { get; set; } = string.Empty;
            public string GiftType { get; set; } = string.Empty;
            public string SoftCredit { get; set; } = string.Empty;
        }

        public class ExportRequest
        {
            public string Fund { get; set; } = string.Empty;
            public List<ExportRow> Rows { get; set; } = new List<ExportRow>();
            public bool IncludeTotal { get; set; } = true;
            public bool IncludeLastDonation { get; set; } = true;
            public bool IncludeEmail { get; set; } = false;
            public bool IncludePhone { get; set; } = false;
            public bool IncludeAddress { get; set; } = false;
            public bool IncludePaymentMethod { get; set; } = false;
            public bool IncludeGiftType { get; set; } = false;
            public bool IncludeSoftCredit { get; set; } = false;
        }

        [HttpPost("export-data")]
        public async Task<IActionResult> ExportData()
        {
            try
            {
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

                if (req == null || req.Rows == null || !req.Rows.Any())
                {
                    return BadRequest("No export data provided.");
                }

                // Log exact fund string provided in request payload
                try { _logger?.LogInformation("DonorExportController: ExportData request for fund='{Fund}'; rows={Count}", req.Fund, req.Rows.Count); } catch { }
                try { Console.WriteLine($"DonorExportController.TRACE: ExportData called with Fund='{req.Fund}' rows={req.Rows.Count}"); } catch { }

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
                if (req.IncludeLastDonation) ws.Cells[1, col++].Value = "LastDonation";
                if (req.IncludePaymentMethod) ws.Cells[1, col++].Value = "PaymentMethod";
                if (req.IncludeGiftType) ws.Cells[1, col++].Value = "GiftType";
                if (req.IncludeSoftCredit) ws.Cells[1, col++].Value = "SoftCredit";
                if (req.IncludeEmail) ws.Cells[1, col++].Value = "Email";
                if (req.IncludePhone) ws.Cells[1, col++].Value = "Phone";
                if (req.IncludeAddress) ws.Cells[1, col++].Value = "Address";

                var rowIndex = 2;
                foreach (var r in req.Rows)
                {
                    col = 1;
                    ws.Cells[rowIndex, col++].Value = r.Name;
                    if (req.IncludeTotal) { ws.Cells[rowIndex, col].Value = r.Total; ws.Cells[rowIndex, col++].Style.Numberformat.Format = "#,##0.00"; }
                    if (req.IncludeLastDonation) { ws.Cells[rowIndex, col].Value = r.LastDonation; ws.Cells[rowIndex, col++].Style.Numberformat.Format = "mm/dd/yyyy"; }
                    if (req.IncludePaymentMethod) ws.Cells[rowIndex, col++].Value = r.PaymentMethod;
                    if (req.IncludeGiftType) ws.Cells[rowIndex, col++].Value = r.GiftType;
                    if (req.IncludeSoftCredit) ws.Cells[rowIndex, col++].Value = r.SoftCredit;
                    if (req.IncludeEmail) ws.Cells[rowIndex, col++].Value = r.Email;
                    if (req.IncludePhone) ws.Cells[rowIndex, col++].Value = r.Phone;
                    if (req.IncludeAddress) ws.Cells[rowIndex, col++].Value = r.Address;
                    rowIndex++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                var bytes = package.GetAsByteArray();
                var fileName = $"donors_{(string.IsNullOrEmpty(req.Fund) ? "all" : req.Fund)}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                // Return the file as a FileContentResult
                var result = File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                return result;
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
