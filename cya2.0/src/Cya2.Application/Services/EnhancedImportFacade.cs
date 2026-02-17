using cya2.Services.Imports; // Main application's import services and types
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Enhanced import facade that integrates with existing import services
/// Provides additional analytics and orchestration capabilities
/// </summary>
public class EnhancedImportFacade
{
    private readonly IDonationImportService _donationImportService;
    private readonly IAccountingImportService _accountingImportService;
    private readonly ILogger<EnhancedImportFacade> _logger;

    public EnhancedImportFacade(
        IDonationImportService donationImportService,
        IAccountingImportService accountingImportService, 
        ILogger<EnhancedImportFacade> logger)
    {
        _donationImportService = donationImportService;
        _accountingImportService = accountingImportService;
        _logger = logger;
    }

    /// <summary>
    /// Enhanced donation import with analytics integration
    /// </summary>
    public async Task<ImportResult> ImportDonationsWithAnalyticsAsync(
        Stream file, 
        string fileName,
        bool runPostAnalysis = true,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting enhanced donation import for file: {FileName}", fileName);
            
            // Use the main application's import service directly
            var result = await _donationImportService.ImportAsync(file, ct);
            
            // Add any additional analytics if successful
            if (runPostAnalysis && result.InsertedRows > 0)
            {
                _logger.LogInformation("Import completed. Rows: {InsertedRows}/{TotalRows}", 
                    result.InsertedRows, result.TotalRows);
                
                // Additional analytics could be added here in the future
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced donation import failed for file: {FileName}", fileName);
            var result = new ImportResult();
            result.Errors.Add($"Enhanced import failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Enhanced accounting import with balance analysis
    /// </summary>
    public async Task<ImportResult> ImportAccountingWithAnalyticsAsync(
        Stream file, 
        string fileName,
        bool runPostAnalysis = true,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting enhanced accounting import for file: {FileName}", fileName);
            
            // Use the main application's import service directly
            var result = await _accountingImportService.ImportAsync(file, ct);
            
            // Add any additional analytics if successful
            if (runPostAnalysis && result.InsertedRows > 0)
            {
                _logger.LogInformation("Accounting import completed. Rows: {InsertedRows}/{TotalRows}", 
                    result.InsertedRows, result.TotalRows);
                
                // Additional balance analytics could be added here in the future
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced accounting import failed for file: {FileName}", fileName);
            var result = new ImportResult();
            result.Errors.Add($"Enhanced import failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Preview file before import
    /// </summary>
    public async Task<FilePreviewResult> PreviewFileAsync(Stream file, string fileName, string contentType, CancellationToken ct = default)
    {
        try
        {
            // Use donation service for preview (can handle most Excel formats)
            return await _donationImportService.PreviewAsync(file, fileName, contentType, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File preview failed for: {FileName}", fileName);
            throw;
        }
    }

    /// <summary>
    /// Import from existing preview
    /// </summary>
    public async Task<ImportResult> ImportFromPreviewAsync(string previewId, bool isDonationImport = true, CancellationToken ct = default)
    {
        try
        {
            if (isDonationImport)
            {
                return await _donationImportService.ImportFromPreviewAsync(previewId, ct);
            }
            else
            {
                return await _accountingImportService.ImportFromPreviewAsync(previewId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import from preview failed for: {PreviewId}", previewId);
            var result = new ImportResult();
            result.Errors.Add($"Import from preview failed: {ex.Message}");
            return result;
        }
    }
}