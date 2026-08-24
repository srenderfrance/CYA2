using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class AccountSnapshotLoader : IAccountSnapshotLoader
{
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly IExpenseReadRepository _expenseReadRepository;

    public AccountSnapshotLoader(
        IDonationReadRepository donationReadRepository,
        IExpenseReadRepository expenseReadRepository)
    {
        _donationReadRepository = donationReadRepository;
        _expenseReadRepository = expenseReadRepository;
    }

    public async Task<AccountDataSnapshot> LoadAsync(
        UserAccountContextAccount account,
        Cya2.Core.ValueObjects.DateRange queryRange,
        AccountSnapshotKey key,
        CancellationToken cancellationToken = default)
    {
        var donations = InternAccountUtility.IsInternFund(account.Fund) &&
                        InternAccountUtility.TryGetInternDesignationName(account.Fund, out var designation)
            ? await _donationReadRepository.GetInternDonationsByDesignationAndDateRangeAsync(
                designation,
                queryRange.StartDate,
                queryRange.EndDate)
            : await _donationReadRepository.GetDonationsByAccountAndDateRangeAsync(
                account.AccountId,
                account.Fund,
                queryRange.StartDate,
                queryRange.EndDate);

        var accounting = await _expenseReadRepository.GetAccountingDataByClassAndDateAsync(
            account.AccountingClass,
            queryRange.StartDate,
            queryRange.EndDate);

        var subAccounts = InternAccountUtility.IsInternFund(account.Fund)
            ? []
            : await _donationReadRepository.GetSubAccountsByAccountIdAsync(account.AccountId);

        var donationSnapshots = (donations ?? [])
            .Select(record => new DonationSnapshot(
                record.Id,
                record.Date,
                record.Frequency,
                record.AccountName,
                record.PaymentMethod,
                record.GiftType,
                record.Amount,
                record.Fund,
                record.Intern,
                record.HonorMemorialName,
                record.Addressee,
                record.SoftCreditName,
                record.Address,
                record.City,
                record.State,
                record.PostalCode,
                record.Country,
                record.Email,
                record.PhoneFixed,
                record.PhoneMobile,
                record.DateCreated,
                record.IsAnonymous))
            .ToList();

        var accountingSnapshots = (accounting ?? [])
            .Select(record => new AccountingSnapshot(
                record.Id,
                record.AccountingClass,
                record.Date,
                record.Num,
                record.Amount,
                record.AccountNumber,
                record.Account,
                record.Type,
                record.DateCreated))
            .ToList();

        var subAccountSnapshots = (subAccounts ?? [])
            .Select(subAccount => new SubAccountSnapshot(
                subAccount.Id,
                subAccount.AccountId,
                subAccount.SubFund,
                subAccount.Kind))
            .ToList();

        return new AccountDataSnapshot(
            key,
            donationSnapshots,
            accountingSnapshots,
            subAccountSnapshots,
            DateTime.UtcNow,
            (donationSnapshots.Count * 512L) +
            (accountingSnapshots.Count * 256L) +
            (subAccountSnapshots.Count * 128L));
    }
}
