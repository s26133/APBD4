using System.Collections.Generic;

namespace LegacyRenewalApp
{
    public interface ISupportFeeCalculator
    {
        (decimal Fee, string Notes) Calculate(string planCode, bool includePremiumSupport);
    }

    public class SupportFeeCalculator : ISupportFeeCalculator
    {
        private readonly Dictionary<string, decimal> _fees = new Dictionary<string, decimal>
        {
            { "START", 250m },
            { "PRO", 400m },
            { "ENTERPRISE", 700m }
        };

        public (decimal Fee, string Notes) Calculate(string planCode, bool includePremiumSupport)
        {
            if (!includePremiumSupport) return (0m, string.Empty);

            decimal fee = _fees.TryGetValue(planCode, out var f) ? f : 0m;
            return (fee, "premium support included; ");
        }
    }
}