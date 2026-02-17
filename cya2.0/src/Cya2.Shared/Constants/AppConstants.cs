namespace Cya2.Shared.Constants;

/// <summary>
/// Application-wide constants for the fundraising system
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Default date format used throughout the application
    /// </summary>
    public const string DateFormat = "MM/dd/yyyy";

    /// <summary>
    /// Extended date format with time
    /// </summary>
    public const string DateTimeFormat = "MM/dd/yyyy HH:mm";

    /// <summary>
    /// Currency format
    /// </summary>
    public const string CurrencyFormat = "C2";

    /// <summary>
    /// Default culture for formatting
    /// </summary>
    public const string DefaultCulture = "en-US";

    /// <summary>
    /// Maximum file size for uploads (in bytes) - 10MB
    /// </summary>
    public const long MaxUploadSize = 10 * 1024 * 1024;

    /// <summary>
    /// Supported file extensions for data imports
    /// </summary>
    public static readonly string[] AllowedImportExtensions = { ".xlsx", ".xls", ".csv" };

    /// <summary>
    /// Default page sizes for data grids
    /// </summary>
    public static readonly int[] PageSizes = { 10, 25, 50, 100 };

    /// <summary>
    /// Default timeout for database operations (seconds)
    /// </summary>
    public const int DefaultDatabaseTimeout = 30;

    /// <summary>
    /// Maximum retry attempts for failed operations
    /// </summary>
    public const int MaxRetryAttempts = 3;
}

/// <summary>
/// Database table and column name constants
/// </summary>
public static class DatabaseConstants
{
    public static class Tables
    {
        public const string Donations = "Donations";
        public const string Accounts = "Accounts";
        public const string SubAccounts = "SubAccounts";
        public const string Users = "Users";
        public const string AccountsUsers = "AccountsUsers";
        public const string Accounting = "Accounting";
    }

    public static class Columns
    {
        public const string Id = "Id";
        public const string AccountId = "AccountId";
        public const string UserId = "UserId";
        public const string Fund = "Fund";
        public const string AccountName = "AccountName";
        public const string Amount = "Amount";
        public const string Date = "Date";
        public const string DateCreated = "DateCreated";
    }
}

/// <summary>
/// UI-related constants
/// </summary>
public static class UIConstants
{
    /// <summary>
    /// Default notification duration in milliseconds
    /// </summary>
    public const int DefaultNotificationDuration = 3000;

    /// <summary>
    /// Debounce delay for search inputs in milliseconds
    /// </summary>
    public const int SearchDebounceDelay = 300;

    /// <summary>
    /// Maximum length for display names
    /// </summary>
    public const int MaxDisplayNameLength = 50;

    /// <summary>
    /// CSS classes for common styling
    /// </summary>
    public static class CssClasses
    {
        public const string ButtonPrimary = "btn-primary";
        public const string ButtonSecondary = "btn-secondary";
        public const string ButtonDanger = "btn-danger";
        public const string TextMuted = "text-muted";
        public const string TextSuccess = "text-success";
        public const string TextDanger = "text-danger";
    }
}

/// <summary>
/// Business rule constants
/// </summary>
public static class BusinessConstants
{
    /// <summary>
    /// Number of months to consider a donor "active"
    /// </summary>
    public const int ActiveDonorMonths = 24;

    /// <summary>
    /// Default number of recent transactions to show
    /// </summary>
    public const int DefaultRecentTransactionCount = 10;

    /// <summary>
    /// Frequency thresholds for donor classification
    /// </summary>
    public static class DonorFrequencyThresholds
    {
        public const int OneTime = 1;
        public const int OccasionalMax = 4;
        // Frequent = more than OccasionalMax
    }
}