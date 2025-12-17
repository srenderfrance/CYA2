using System;

namespace ModelsLibrary
{
    public class AccountingDataModel
    {
        public required int Id { get; set; }
        public required string AccountingClass { get; set; } //is named "class" in the QuickBooks csv
        public required DateTime Date { get; set; }
        public required string Num { get; set; }
        public required double Amount { get; set; }
        public required string AccountNumber { get; set; }
        public required string Account { get; set; }
        public required string Type { get; set; }
        public required DateTime DateCreated { get; set; }

    }
}
