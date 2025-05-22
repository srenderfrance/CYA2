using System.Diagnostics.CodeAnalysis;


namespace UtilityClassLibrary;

public class UserDataExpenses
{
    public required string StaffAccount {  get; set; } //derived from 'designation'
    public required double Amount { get; set; }
    public required DateTime Date { get; set; }
    public required string Description { get; set; } //derived from 'account'
    public required string Account { get; set; }
    public required string Num {  get; set; }
    public required string AccountNumber { get; set; }

    [SetsRequiredMembers]
    public UserDataExpenses(string staffAccount, double amount, DateTime date, string description, string account, string num, string accountNumber )
    {
        StaffAccount = staffAccount;
        Amount = amount;
        Date = date;    
        Description = description;
        Account = account;
        Num = num;
        AccountNumber = accountNumber;
            
    }

}
