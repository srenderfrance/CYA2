using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModelsLibrary
{
    public class Account
    {
        public int AccountId { get; set; }
        
        [Required]
        public string Fund { get; set; } = string.Empty; // Corresponds to "Fund Notes" from Donation DataTable
        
        [Required]
        public string AccountingClass { get; set; } = string.Empty; // Corresponds to "Class" from the Accounting DataTable
        
        [Required]
        public string AccountNumber { get; set; } = string.Empty; //Corresponds to Account Number from the Accounting DataTable
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // New persisted balance column (currency stored as decimal)
        public decimal Overhead { get; set; }
        
        // Additional properties
        public string SoftCredit { get; set; } = string.Empty;
        public decimal BalanceAdjustment { get; set; }
        public bool OtherFunds { get; set; } = false;

        // Parameterless constructor
        public Account() { }

        // Constructor with required members
        [SetsRequiredMembers]
        public Account(string fund, string accountingClass, string accountNumber, decimal overhead)
        {
            Fund = fund;
            AccountingClass = accountingClass;
            AccountNumber = accountNumber;
            CreatedAt = DateTime.Now;
            Overhead = overhead;
        }
    }
}
