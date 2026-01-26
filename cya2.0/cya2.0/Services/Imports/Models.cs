using System;
using System.Collections.Generic;

namespace cya2.Services.Imports
{
    internal enum ImportType
    {
        Donations,
        Accounting
    }

    internal sealed class ImportJob
    {
        public required ImportType Type { get; init; }
        public required string FilePath { get; init; }
        public required string SubmittedBy { get; init; }
        public DateTime SubmittedAtUtc { get; init; } = DateTime.UtcNow;
    }

    public sealed class ImportResult
    {
        public int TotalRows { get; set; }
        public int InsertedRows { get; set; }
        public int FailedRows { get; set; }
        public List<string> Errors { get; } = new();
        public string? ProgressId { get; set; }
    }

    internal sealed class AccountingRowDto
    {
        public required string AccountingClass { get; init; }
        public required DateTime Date { get; init; }
        public required string Num { get; init; }
        public required double Amount { get; init; }
        public required string AccountNumber { get; init; }
        public required string Account { get; init; }
        public required string Type { get; init; }
        public DateTime DateCreated { get; init; } = DateTime.UtcNow;
    }

    internal sealed class DonationRowDto
    {
        public required DateTime Date { get; init; }
        public required string AccountName { get; init; }
        public required string PaymentMethod { get; init; }
        public required string GiftType { get; init; }
        public required double Amount { get; init; }
        public required string Fund { get; init; }
        public string? SoftCreditName { get; init; }
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? PostalCode { get; init; }
        public string? Country { get; init; }
        public string? Email { get; init; }
        public string? PhoneFixed { get; init; }
        public string? PhoneMobile { get; init; }
        public bool IsAnonymous { get; init; }
        public DateTime DateCreated { get; init; } = DateTime.UtcNow;
    }
}
