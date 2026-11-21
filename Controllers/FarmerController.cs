using System;
using System.Data;
using System.Text.RegularExpressions;
using Cyusa_Divince_Lab3.Models;

namespace Cyusa_Divince_Lab3.Controllers
{
    public sealed class FarmerController
    {
        public DataTable Search(string searchText)
        {
            return Database.Query(
                "SELECT id,member_number,name,phone,address FROM farmers WHERE member_number ILIKE @q OR name ILIKE @q ORDER BY name",
                Database.Parameter("q", "%" + (searchText ?? string.Empty).Trim() + "%"));
        }

        public void Save(Farmer farmer)
        {
            if (farmer == null || string.IsNullOrWhiteSpace(farmer.Name) ||
                string.IsNullOrWhiteSpace(farmer.Phone) || string.IsNullOrWhiteSpace(farmer.Address))
                throw new ArgumentException("All farmer fields are required.");

            if (!Regex.IsMatch(farmer.Phone.Trim(), @"^\d{10}$"))
                throw new ArgumentException("Please check the entered information.");

            Database.Execute(
                "INSERT INTO farmers(name,phone,address) VALUES(@n,@p,@a)",
                Database.Parameter("n", farmer.Name.Trim()),
                Database.Parameter("p", farmer.Phone.Trim()),
                Database.Parameter("a", farmer.Address.Trim()));
        }
    }
}
