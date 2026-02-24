namespace Cya2.Core.Enums;

/// <summary>
/// Represents the giving frequency pattern of a donor based on their complete donation history
/// </summary>
public enum DonorFrequency
{
    /// <summary>
    /// No donations or invalid state
    /// </summary>
    None,
    
    /// <summary>
    /// Single donation with no follow-up gifts
    /// </summary>
    OneTime,
    
    /// <summary>
    /// Multiple donations but irregular timing (not monthly or yearly pattern)
    /// </summary>
    Sporadic,
    
    /// <summary>
    /// Regular monthly giving pattern (with allowance for catch-up donations)
    /// </summary>
    Monthly,
    
    /// <summary>
    /// Annual giving pattern - one or more gifts per year around same time
    /// </summary>
    Yearly,
    
    /// <summary>
    /// Legacy value for backward compatibility - maps to Sporadic
    /// </summary>
    [Obsolete("Use Sporadic instead")]
    Occasional,
    
    /// <summary>
    /// Legacy value for backward compatibility - maps to Monthly
    /// </summary>
    [Obsolete("Use Monthly instead")]
    Frequent
}