using System;

namespace LegacyRenewalApp
{
    public class SubscriptionRenewalService
    {
        private readonly ITaxCalculator _taxCalculator;
        private readonly IPaymentFeeCalculator _paymentFeeCalculator;
        private readonly ISupportFeeCalculator _supportFeeCalculator;

        public SubscriptionRenewalService() : this(
            new TaxCalculator(),
            new PaymentFeeCalculator(),
            new SupportFeeCalculator())
        {
        }

        public SubscriptionRenewalService(
            ITaxCalculator taxCalculator,
            IPaymentFeeCalculator paymentFeeCalculator,
            ISupportFeeCalculator supportFeeCalculator)
        {
            _taxCalculator = taxCalculator;
            _paymentFeeCalculator = paymentFeeCalculator;
            _supportFeeCalculator = supportFeeCalculator;
        }

        public RenewalInvoice CreateRenewalInvoice(
            int customerId,
            string planCode,
            int seatCount,
            string paymentMethod,
            bool includePremiumSupport,
            bool useLoyaltyPoints)
        {
            if (customerId <= 0) throw new ArgumentException("Customer id must be positive");
            if (string.IsNullOrWhiteSpace(planCode)) throw new ArgumentException("Plan code is required");
            if (seatCount <= 0) throw new ArgumentException("Seat count must be positive");
            if (string.IsNullOrWhiteSpace(paymentMethod)) throw new ArgumentException("Payment method is required");

            string normalizedPlanCode = planCode.Trim().ToUpperInvariant();
            string normalizedPaymentMethod = paymentMethod.Trim().ToUpperInvariant();

            ICustomerRepository customerRepository = new CustomerRepository();
            ISubscriptionPlanRepository planRepository = new SubscriptionPlanRepository();

            var customer = customerRepository.GetById(customerId);
            var plan = planRepository.GetByCode(normalizedPlanCode);

            if (!customer.IsActive)
            {
                throw new InvalidOperationException("Inactive customers cannot renew subscriptions");
            }

            decimal baseAmount = (plan.MonthlyPricePerSeat * seatCount * 12m) + plan.SetupFee;
            decimal discountAmount = 0m;
            string notes = string.Empty;

            if (customer.Segment == "Silver") { discountAmount += baseAmount * 0.05m; notes += "silver discount; "; }
            else if (customer.Segment == "Gold") { discountAmount += baseAmount * 0.10m; notes += "gold discount; "; }
            else if (customer.Segment == "Platinum") { discountAmount += baseAmount * 0.15m; notes += "platinum discount; "; }
            else if (customer.Segment == "Education" && plan.IsEducationEligible) { discountAmount += baseAmount * 0.20m; notes += "education discount; "; }

            if (customer.YearsWithCompany >= 5) { discountAmount += baseAmount * 0.07m; notes += "long-term loyalty discount; "; }
            else if (customer.YearsWithCompany >= 2) { discountAmount += baseAmount * 0.03m; notes += "basic loyalty discount; "; }

            if (seatCount >= 50) { discountAmount += baseAmount * 0.12m; notes += "large team discount; "; }
            else if (seatCount >= 20) { discountAmount += baseAmount * 0.08m; notes += "medium team discount; "; }
            else if (seatCount >= 10) { discountAmount += baseAmount * 0.04m; notes += "small team discount; "; }

            if (useLoyaltyPoints && customer.LoyaltyPoints > 0)
            {
                int pointsToUse = Math.Min(customer.LoyaltyPoints, 200);
                discountAmount += pointsToUse;
                notes += $"loyalty points used: {pointsToUse}; ";
            }

            decimal subtotalAfterDiscount = baseAmount - discountAmount;
            if (subtotalAfterDiscount < 300m)
            {
                subtotalAfterDiscount = 300m;
                notes += "minimum discounted subtotal applied; ";
            }

            var supportFeeResult = _supportFeeCalculator.Calculate(normalizedPlanCode, includePremiumSupport);
            decimal supportFee = supportFeeResult.Fee;
            notes += supportFeeResult.Notes;

            var paymentFeeResult = _paymentFeeCalculator.Calculate(normalizedPaymentMethod, subtotalAfterDiscount + supportFee);
            decimal paymentFee = paymentFeeResult.Fee;
            notes += paymentFeeResult.Notes;

            decimal taxRate = _taxCalculator.GetTaxRate(customer.Country);
            decimal taxBase = subtotalAfterDiscount + supportFee + paymentFee;
            decimal taxAmount = taxBase * taxRate;
            decimal finalAmount = taxBase + taxAmount;

            if (finalAmount < 500m)
            {
                finalAmount = 500m;
                notes += "minimum invoice amount applied; ";
            }

            var invoice = new RenewalInvoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{customerId}-{normalizedPlanCode}",
                CustomerName = customer.FullName,
                PlanCode = normalizedPlanCode,
                PaymentMethod = normalizedPaymentMethod,
                SeatCount = seatCount,
                BaseAmount = Math.Round(baseAmount, 2, MidpointRounding.AwayFromZero),
                DiscountAmount = Math.Round(discountAmount, 2, MidpointRounding.AwayFromZero),
                SupportFee = Math.Round(supportFee, 2, MidpointRounding.AwayFromZero),
                PaymentFee = Math.Round(paymentFee, 2, MidpointRounding.AwayFromZero),
                TaxAmount = Math.Round(taxAmount, 2, MidpointRounding.AwayFromZero),
                FinalAmount = Math.Round(finalAmount, 2, MidpointRounding.AwayFromZero),
                Notes = notes.Trim(),
                GeneratedAt = DateTime.UtcNow
            };

            IBillingGateway billingGateway = new BillingGatewayWrapper();
            billingGateway.SaveInvoice(invoice);

            if (!string.IsNullOrWhiteSpace(customer.Email))
            {
                string subject = "Subscription renewal invoice";
                string body = $"Hello {customer.FullName}, your renewal for plan {normalizedPlanCode} has been prepared. Final amount: {invoice.FinalAmount:F2}.";
                billingGateway.SendEmail(customer.Email, subject, body);
            }

            return invoice;
        }
    }
}