using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

    public class DonationsDataModel
{
        public required int id { get; set; }
        public required DateTime date { get; set; }
        public required string accountname { get; set; }
        public required string gifttype{ get; set; }
        public required string recurring { get; set; }
        public required double amount { get; set; }
        public required string fund { get; set; }
        public required string softcreditname { get; set; }
        public string? address { get; set; }
        public string? city { get; set; }
        public string? state { get; set; }
        public string? postalcode { get; set; }
        public string? country { get; set; }
        public  string? email { get; set; }
        public string? phonefixed { get; set; }
        public string? phonemobile { get; set; }
        public required DateTime datecreated { get; set; }
    }
