using System.ComponentModel.DataAnnotations;

namespace ModelsLibrary
{
    /// <summary>
    /// Database model for SubAccounts table.
    /// </summary>
    public class SubAccount
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AccountId { get; set; }

        // UI refers to this as "Fund" for sub-funds; DB column name is SubFund
        [Required]
        public string SubFund { get; set; } = string.Empty;

        // UI refers to this as "Type"; stored as string in DB (e.g., "Merged", "Separate")
        [Required]
        public string Kind { get; set; } = string.Empty;
    }
}
