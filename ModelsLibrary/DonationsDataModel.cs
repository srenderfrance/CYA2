using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

public class DonationsDataModel
{
    public required int Id { get; set; }
    public required DateTime Date { get; set; }
    public required string AccountName { get; set; }
    public required string PaymentMethod { get; set; }
    public required string GiftType { get; set; }
    public required double Amount { get; set; }
    public required string Fund { get; set; }
    public string? SoftCreditName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? PhoneFixed { get; set; }
    public string? PhoneMobile { get; set; }
    public required DateTime DateCreated { get; set; }
    public bool IsAnonymous { get; set; } = false;
}
