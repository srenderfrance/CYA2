using System.Threading;
using System.Threading.Tasks;
using cya2.Services.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/upload")]
    [Authorize(Roles = "Admin")]
    public sealed class UploadController : ControllerBase
    {
        private readonly IDonationImportService _donationService;
        private readonly IAccountingImportService _accountingService;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            IDonationImportService donationService,
            IAccountingImportService accountingService,
            ILogger<UploadController> logger)
        {
            _donationService = donationService;
            _accountingService = accountingService;
            _logger = logger;
        }

        [HttpPost("donations/preview")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<ActionResult<FilePreviewResult>> PreviewDonations([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            _logger.LogInformation("Donation preview upload: {FileName}, {Size} bytes, {ContentType}",
                file.FileName, file.Length, file.ContentType ?? "");

            await using var stream = file.OpenReadStream();
            var preview = await _donationService.PreviewAsync(stream, file.FileName, file.ContentType ?? string.Empty, ct);
            return Ok(preview);
        }

        [HttpPost("donations/confirm")]
        public async Task<ActionResult<ImportResult>> ConfirmDonations([FromBody] ConfirmImportRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PreviewId))
            {
                return BadRequest("PreviewId is required");
            }

            _logger.LogInformation("Confirming donation import for preview {PreviewId}", request.PreviewId);
            var result = await _donationService.ImportFromPreviewAsync(request.PreviewId, ct);
            return Ok(result);
        }

        [HttpPost("accounting/preview")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<ActionResult<FilePreviewResult>> PreviewAccounting([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            _logger.LogInformation("Accounting preview upload: {FileName}, {Size} bytes, {ContentType}",
                file.FileName, file.Length, file.ContentType ?? "");

            await using var stream = file.OpenReadStream();
            var preview = await _accountingService.PreviewAsync(stream, file.FileName, file.ContentType ?? string.Empty, ct);
            return Ok(preview);
        }

        [HttpPost("accounting/confirm")]
        public async Task<ActionResult<ImportResult>> ConfirmAccounting([FromBody] ConfirmImportRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PreviewId))
            {
                return BadRequest("PreviewId is required");
            }

            _logger.LogInformation("Confirming accounting import for preview {PreviewId}", request.PreviewId);
            var result = await _accountingService.ImportFromPreviewAsync(request.PreviewId, ct);
            return Ok(result);
        }
    }

    public sealed class ConfirmImportRequest
    {
        public string PreviewId { get; set; } = string.Empty;
    }
}
