using Cya2.Core.Enums;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Enhanced import orchestration service that integrates with clean architecture services
/// </summary>
public interface IImportOrchestrationService
{
    /// <summary>
    /// Orchestrate donation import with business logic integration
    /// </summary>
    Task<EnhancedImportResult> ImportDonationsWithAnalysisAsync(Stream file, string fileName, ImportOptions options, CancellationToken ct = default);
    
    /// <summary>
    /// Orchestrate accounting import with balance calculation integration
    /// </summary>
    Task<EnhancedImportResult> ImportAccountingWithAnalysisAsync(Stream file, string fileName, ImportOptions options, CancellationToken ct = default);
    
    /// <summary>
    /// Get import analysis and preview before actual import
    /// </summary>
    Task<ImportAnalysisResult> AnalyzeImportFileAsync(Stream file, string fileName, ImportType importType, CancellationToken ct = default);
    
    /// <summary>
    /// Execute import from previous analysis
    /// </summary>
    Task<EnhancedImportResult> ExecuteImportFromAnalysisAsync(string analysisId, ImportOptions options, CancellationToken ct = default);
    
    /// <summary>
    /// Post-import analysis and cache warming
    /// </summary>
    Task<PostImportAnalysisResult> RunPostImportAnalysisAsync(EnhancedImportResult importResult, CancellationToken ct = default);
}

/// <summary>
/// Import options for enhanced processing
/// </summary>
public class ImportOptions
{
    public bool RunPostImportAnalysis { get; set; } = true;
    public bool UpdateDonorProfiles { get; set; } = true;
    public bool RecalculateAccountBalances { get; set; } = true;
    public bool WarmAnalyticsCache { get; set; } = true;
    public bool GenerateImportSummary { get; set; } = true;
    public List<int> AffectedAccountIds { get; set; } = new();
}

/// <summary>
/// Enhanced import result with business logic integration
/// </summary>
public class EnhancedImportResult
{
    public bool IsSuccess { get; set; }
    public int TotalRows { get; set; }
    public int InsertedRows { get; set; }
    public int FailedRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? ProgressId { get; set; }
    public DateTime ImportStarted { get; set; }
    public DateTime ImportCompleted { get; set; }
    public TimeSpan Duration => ImportCompleted - ImportStarted;
    
    // Enhanced analytics
    public ImportSummaryAnalytics? Analytics { get; set; }
    public List<DonorProfileImpact>? DonorImpacts { get; set; }
    public List<AccountBalanceImpact>? AccountImpacts { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Pre-import analysis result
/// </summary>
public class ImportAnalysisResult
{
    public string AnalysisId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public int EstimatedRows { get; set; }
    public DateTime? EarliestDate { get; set; }
    public DateTime? LatestDate { get; set; }
    public List<string> DetectedAccounts { get; set; } = new();
    public List<string> DetectedDonors { get; set; } = new(); // For donation imports
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public decimal EstimatedDataVolume { get; set; }
    public ImportImpactPreview ImpactPreview { get; set; } = new();
}

/// <summary>
/// Preview of import impact
/// </summary>
public class ImportImpactPreview
{
    public List<int> AffectedAccountIds { get; set; } = new();
    public int EstimatedDonorsAffected { get; set; }
    public int EstimatedNewDonors { get; set; }
    public decimal EstimatedTotalAmount { get; set; }
    public List<string> AccountsWithBalanceChanges { get; set; } = new();
}

/// <summary>
/// Import summary analytics
/// </summary>
public class ImportSummaryAnalytics
{
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
    public int UniqueAccounts { get; set; }
    public int UniqueDonors { get; set; }
    public Dictionary<string, int> TransactionsByAccount { get; set; } = new();
    public Dictionary<string, decimal> AmountsByAccount { get; set; } = new();
    public DateTimeOffset OldestTransaction { get; set; }
    public DateTimeOffset NewestTransaction { get; set; }
}

/// <summary>
/// Donor profile impact from import
/// </summary>
public class DonorProfileImpact
{
    public string DonorName { get; set; } = string.Empty;
    public bool IsNewDonor { get; set; }
    public DonorFrequency PreviousFrequency { get; set; }
    public DonorFrequency NewFrequency { get; set; }
    public bool FrequencyChanged => PreviousFrequency != NewFrequency;
    public int NewDonationCount { get; set; }
    public decimal NewDonationTotal { get; set; }
}

/// <summary>
/// Account balance impact from import
/// </summary>
public class AccountBalanceImpact
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public decimal BalanceChange => NewBalance - PreviousBalance;
    public int TransactionCount { get; set; }
}

/// <summary>
/// Post-import analysis result
/// </summary>
public class PostImportAnalysisResult
{
    public bool IsSuccess { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<DonorAlert> DonorAlerts { get; set; } = new();
    public List<AccountAlert> AccountAlerts { get; set; } = new();
    public CacheWarmupResult CacheWarmup { get; set; } = new();
}

/// <summary>
/// Donor alert from import analysis
/// </summary>
public class DonorAlert
{
    public string DonorName { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty; // "FrequencyChange", "MissedMonths", "NewMajorDonor"
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // "Info", "Warning", "Alert"
}

/// <summary>
/// Account alert from import analysis
/// </summary>
public class AccountAlert
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty; // "BalanceChange", "LowBalance", "HighActivity"
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

/// <summary>
/// Cache warmup result
/// </summary>
public class CacheWarmupResult
{
    public bool IsSuccess { get; set; }
    public int AccountsWarmedUp { get; set; }
    public int DonorProfilesGenerated { get; set; }
    public TimeSpan WarmupDuration { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Import type enumeration
/// </summary>
public enum ImportType
{
    Donations,
    Accounting
}