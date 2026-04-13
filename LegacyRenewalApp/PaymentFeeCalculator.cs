using System;
using System.Collections.Generic;

namespace LegacyRenewalApp
{
    public interface IPaymentFeeCalculator
    {
        (decimal Fee, string Notes) Calculate(string paymentMethod, decimal baseAmount);
    }

    public class PaymentFeeCalculator : IPaymentFeeCalculator
    {
        private readonly Dictionary<string, (decimal Rate, string Notes)> _fees = new Dictionary<string, (decimal, string)>
        {
            { "CARD", (0.02m, "card payment fee; ") },
            { "BANK_TRANSFER", (0.01m, "bank transfer fee; ") },
            { "PAYPAL", (0.035m, "paypal fee; ") },
            { "INVOICE", (0m, "invoice payment; ") }
        };

        public (decimal Fee, string Notes) Calculate(string paymentMethod, decimal baseAmount)
        {
            if (_fees.TryGetValue(paymentMethod, out var fee))
            {
                return (baseAmount * fee.Rate, fee.Notes);
            }

            throw new ArgumentException("Unsupported payment method");
        }
    }
}