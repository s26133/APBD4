using System.Collections.Generic;

namespace LegacyRenewalApp
{
    public interface ITaxCalculator
    {
        decimal GetTaxRate(string country);
    }

    public class TaxCalculator : ITaxCalculator
    {
        private readonly Dictionary<string, decimal> _rates = new Dictionary<string, decimal>
        {
            { "Poland", 0.23m },
            { "Germany", 0.19m },
            { "Czech Republic", 0.21m },
            { "Norway", 0.25m }
        };

        public decimal GetTaxRate(string country)
        {
            return _rates.TryGetValue(country, out var rate) ? rate : 0.20m;
        }
    }
}