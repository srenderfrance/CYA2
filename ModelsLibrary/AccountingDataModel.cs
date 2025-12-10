namespace ModelsLibrary
{
    public class AccountingDataModel
    {
        public required int id { get; set; }
        public required string designation { get; set; } //is named "class" in the QuickBooks csv
        public required DateTime date { get; set; }
        public required string num { get; set; }
        public required double amount { get; set; }
        public required string accountnum { get; set; }
        public required string account { get; set; }
        public required string type { get; set; }
        public required DateTime dateCreated { get; set; }

    }
}
