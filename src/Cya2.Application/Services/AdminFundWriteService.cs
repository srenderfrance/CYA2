using Cya2.Application.DTOs;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;

namespace Cya2.Application.Services;

public class AdminFundWriteService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ISubAccountRepository _subAccountRepository;

    public AdminFundWriteService(IAccountRepository accountRepository, ISubAccountRepository subAccountRepository)
    {
        _accountRepository = accountRepository;
        _subAccountRepository = subAccountRepository;
    }

    public async Task<AdminFundOperationDto> CreatePrimaryFundAsync(AdminFundUpsertDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Fund)) return new AdminFundOperationDto { IsSuccess = false, Message = "Fund name is required" };
        if (string.IsNullOrWhiteSpace(request.AccountNumber)) return new AdminFundOperationDto { IsSuccess = false, Message = "Account number is required" };
        if (string.IsNullOrWhiteSpace(request.AccountingClass)) return new AdminFundOperationDto { IsSuccess = false, Message = "Account reference (Class) is required" };

        var fund = request.Fund.Trim();
        var accountNumber = request.AccountNumber.Trim();

        var existingFund = await _accountRepository.GetByFundAsync(fund);
        if (existingFund != null)
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A fund with the name '{fund}' already exists" };

        var existingAccountNumber = await _accountRepository.GetByAccountNumberAsync(accountNumber);
        if (existingAccountNumber != null)
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A fund with the number '{accountNumber}' already exists" };

        var entity = new Account
        {
            Fund = fund,
            AccountingClass = request.AccountingClass.Trim(),
            AccountNumber = accountNumber,
            SoftCredit = request.SoftCredit?.Trim() ?? string.Empty,
            BalanceAdjustment = request.BalanceAdjustment,
            Overhead = request.Overhead,
            CreatedAt = DateTime.UtcNow
        };

        await _accountRepository.AddAsync(entity);
        return new AdminFundOperationDto { IsSuccess = true, Message = $"New fund '{fund}' added successfully" };
    }

    public async Task<AdminFundOperationDto> UpdatePrimaryFundAsync(int accountId, AdminFundUpsertDto request)
    {
        if (accountId <= 0) return new AdminFundOperationDto { IsSuccess = false, Message = "Selected fund not found" };

        var current = await _accountRepository.GetByIdAsync(accountId);
        if (current == null) return new AdminFundOperationDto { IsSuccess = false, Message = "Selected fund not found" };

        var fund = request.Fund?.Trim() ?? string.Empty;
        var accountNumber = request.AccountNumber?.Trim() ?? string.Empty;
        var accountingClass = request.AccountingClass?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fund)) return new AdminFundOperationDto { IsSuccess = false, Message = "Fund name is required" };
        if (string.IsNullOrWhiteSpace(accountNumber)) return new AdminFundOperationDto { IsSuccess = false, Message = "Account number is required" };
        if (string.IsNullOrWhiteSpace(accountingClass)) return new AdminFundOperationDto { IsSuccess = false, Message = "Account reference (Class) is required" };

        var existingFund = await _accountRepository.GetByFundAsync(fund);
        if (existingFund != null && existingFund.AccountId != accountId)
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A fund with the name '{fund}' already exists" };

        var existingAccountNumber = await _accountRepository.GetByAccountNumberAsync(accountNumber);
        if (existingAccountNumber != null && existingAccountNumber.AccountId != accountId)
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A fund with the number '{accountNumber}' already exists" };

        current.Fund = fund;
        current.AccountingClass = accountingClass;
        current.AccountNumber = accountNumber;
        current.SoftCredit = request.SoftCredit?.Trim() ?? string.Empty;
        current.BalanceAdjustment = request.BalanceAdjustment;
        current.Overhead = request.Overhead;

        await _accountRepository.UpdateAsync(current);
        return new AdminFundOperationDto { IsSuccess = true, Message = "Fund updated successfully" };
    }

    public async Task<AdminFundOperationDto> DeleteFundAsync(int accountId)
    {
        if (accountId <= 0) return new AdminFundOperationDto { IsSuccess = false, Message = "Selected fund not found" };

        var current = await _accountRepository.GetByIdAsync(accountId);
        if (current == null) return new AdminFundOperationDto { IsSuccess = false, Message = "Selected fund not found" };

        await _accountRepository.DeleteAsync(accountId);
        return new AdminFundOperationDto { IsSuccess = true, Message = "Fund deleted successfully" };
    }

    public async Task<AdminFundOperationDto> CreateSubFundAsync(int accountId, string subFund, string kind)
    {
        if (accountId <= 0) return new AdminFundOperationDto { IsSuccess = false, Message = "Please select a principle fund" };
        if (string.IsNullOrWhiteSpace(subFund)) return new AdminFundOperationDto { IsSuccess = false, Message = "Sub Fund name is required" };
        if (string.IsNullOrWhiteSpace(kind)) return new AdminFundOperationDto { IsSuccess = false, Message = "Sub Fund type is required" };

        var trimmed = subFund.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 255)
            return new AdminFundOperationDto { IsSuccess = false, Message = "Sub Fund name must be between 2 and 255 characters" };

        if (await _subAccountRepository.ExistsByNameAsync(accountId, trimmed))
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A sub fund named '{trimmed}' already exists for the selected fund" };

        await _subAccountRepository.AddAsync(new SubAccount(accountId, trimmed, kind.Trim()));
        return new AdminFundOperationDto { IsSuccess = true, Message = $"Sub fund '{trimmed}' added to selected fund" };
    }

    public async Task<AdminFundOperationDto> UpdateSubFundAsync(int subAccountId, string subFund, string kind)
    {
        if (subAccountId <= 0) return new AdminFundOperationDto { IsSuccess = false, Message = "Please select a subaccount to update" };
        if (string.IsNullOrWhiteSpace(subFund)) return new AdminFundOperationDto { IsSuccess = false, Message = "Subaccount name is required" };
        if (string.IsNullOrWhiteSpace(kind)) return new AdminFundOperationDto { IsSuccess = false, Message = "Subaccount type is required" };

        var trimmed = subFund.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 255)
            return new AdminFundOperationDto { IsSuccess = false, Message = "Subaccount name must be between 2 and 255 characters" };

        var current = await _subAccountRepository.GetByIdAsync(subAccountId);
        if (current == null) return new AdminFundOperationDto { IsSuccess = false, Message = "Please select a subaccount to update" };

        if (await _subAccountRepository.ExistsByNameAsync(current.AccountId, trimmed, subAccountId))
            return new AdminFundOperationDto { IsSuccess = false, Message = $"A subaccount named '{trimmed}' already exists for this fund" };

        current.SubFund = trimmed;
        current.Kind = kind.Trim();
        await _subAccountRepository.UpdateAsync(current);

        return new AdminFundOperationDto { IsSuccess = true, Message = "Subaccount updated successfully" };
    }

    public async Task<AdminFundOperationDto> DeleteSubFundAsync(int subAccountId)
    {
        if (subAccountId <= 0) return new AdminFundOperationDto { IsSuccess = false, Message = "Please select a subaccount to delete" };

        var current = await _subAccountRepository.GetByIdAsync(subAccountId);
        if (current == null) return new AdminFundOperationDto { IsSuccess = false, Message = "Please select a subaccount to delete" };

        var deleted = await _subAccountRepository.DeleteAsync(subAccountId);
        return deleted
            ? new AdminFundOperationDto { IsSuccess = true, Message = "Subaccount deleted" }
            : new AdminFundOperationDto { IsSuccess = false, Message = "Failed to delete subaccount" };
    }
}
