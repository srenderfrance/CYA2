using System.Collections.ObjectModel;
using Cya2.Core.Enums;

namespace Cya2.Application.Models;

public sealed class AccountDataSnapshot
{
    public AccountDataSnapshot(
        AccountSnapshotKey key,
        IEnumerable<DonationSnapshot> donations,
        IEnumerable<AccountingSnapshot> accounting,
        IEnumerable<SubAccountSnapshot> subAccounts,
        DateTime createdUtc,
        long approximateBytes)
    {
        Key = key;
        Donations = new ReadOnlyCollection<DonationSnapshot>((donations ?? []).ToArray());
        Accounting = new ReadOnlyCollection<AccountingSnapshot>((accounting ?? []).ToArray());
        SubAccounts = new ReadOnlyCollection<SubAccountSnapshot>((subAccounts ?? []).ToArray());
        CreatedUtc = createdUtc;
        ApproximateBytes = Math.Max(0, approximateBytes);
    }

    public AccountSnapshotKey Key { get; }
    public IReadOnlyList<DonationSnapshot> Donations { get; }
    public IReadOnlyList<AccountingSnapshot> Accounting { get; }
    public IReadOnlyList<SubAccountSnapshot> SubAccounts { get; }
    public DateTime CreatedUtc { get; }
    public long ApproximateBytes { get; }
}

public sealed record DonationSnapshot(
    int Id,
    DateTime Date,
    DonorFrequency? Frequency,
    string AccountName,
    string PaymentMethod,
    string GiftType,
    double Amount,
    string Fund,
    string? Intern,
    string? HonorMemorialName,
    string? Addressee,
    string? SoftCreditName,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Email,
    string? PhoneFixed,
    string? PhoneMobile,
    DateTime DateCreated,
    bool IsAnonymous);

public sealed record AccountingSnapshot(
    int Id,
    string AccountingClass,
    DateTime Date,
    string Num,
    double Amount,
    string AccountNumber,
    string Account,
    string Type,
    DateTime DateCreated);

public sealed record SubAccountSnapshot(
    int Id,
    int AccountId,
    string SubFund,
    string Kind);
