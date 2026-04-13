using System;

namespace LegacyRenewalApp
{
    public class SubscriptionRenewalService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly ISubscriptionPlanRepository _planRepository;
        private readonly IBillingGateway _billingGateway;
        private readonly IDiscountCalculator _discountCalculator;
        private readonly ISupportFeeCalculator _supportFeeCalculator;
        private readonly IPaymentFeeCalculator _paymentFeeCalculator;
        private readonly ITaxCalculator _taxCalculator;

        public SubscriptionRenewalService() : this(
            new CustomerRepository(),
            new SubscriptionPlanRepository(),
            new BillingGatewayWrapper(),
            new DiscountCalculator(),
            new SupportFeeCalculator(),
            new PaymentFeeCalculator(),
            new TaxCalculator())
        {
        }

        public SubscriptionRenewalService(
            ICustomerRepository customerRepository,
            ISubscriptionPlanRepository planRepository,
            IBillingGateway billingGateway,
            IDiscountCalculator discountCalculator,
            ISupportFeeCalculator supportFeeCalculator,
            IPaymentFeeCalculator paymentFeeCalculator,
            ITaxCalculator taxCalculator)
        {
            _customerRepository = customerRepository;
            _planRepository = planRepository;
            _billingGateway = billingGateway;
            _discountCalculator = discountCalculator;
            _supportFeeCalculator = supportFeeCalculator;
            _paymentFeeCalculator = paymentFeeCalculator;
            _taxCalculator = taxCalculator;
        }

        public RenewalInvoice CreateRenewalInvoice(
            int customerId,
            string planCode,
            int seatCount,
            string paymentMethod,
            bool includePremiumSupport,
            bool useLoyaltyPoints)
        {
            ValidateInputs(customerId, planCode, seatCount, paymentMethod);

            string normalizedPlanCode = planCode.Trim().ToUpperInvariant();
            string normalizedPaymentMethod = paymentMethod.Trim().ToUpperInvariant();

            var customer = _customerRepository.GetById(customerId);
            var plan = _planRepository.GetByCode(normalizedPlanCode);

            if (!customer.IsActive)
            {
                throw new InvalidOperationException("Inactive customers cannot renew subscriptions");
            }

            decimal baseAmount = (plan.MonthlyPricePerSeat * seatCount * 12m) + plan.SetupFee;
            var discountResult = _discountCalculator.Calculate(customer, plan, seatCount, baseAmount, useLoyaltyPoints);

            decimal subtotal = baseAmount - discountResult.Amount;
            string subtotalNotes = string.Empty;
            if (subtotal < 300m)
            {
                subtotal = 300m;
                subtotalNotes = "minimum discounted subtotal applied; ";
            }

            var supportFeeResult = _supportFeeCalculator.Calculate(normalizedPlanCode, includePremiumSupport);
            var paymentFeeResult = _paymentFeeCalculator.Calculate(normalizedPaymentMethod, subtotal + supportFeeResult.Fee);

            decimal taxRate = _taxCalculator.GetTaxRate(customer.Country);
            decimal taxBase = subtotal + supportFeeResult.Fee + paymentFeeResult.Fee;
            decimal taxAmount = taxBase * taxRate;
            decimal finalAmount = taxBase + taxAmount;

            string finalNotes = string.Empty;
            if (finalAmount < 500m)
            {
                finalAmount = 500m;
                finalNotes = "minimum invoice amount applied; ";
            }

            string allNotes = (discountResult.Notes + subtotalNotes + supportFeeResult.Notes + paymentFeeResult.Notes + finalNotes).Trim();

            var invoice = new RenewalInvoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{customerId}-{normalizedPlanCode}",
                CustomerName = customer.FullName,
                PlanCode = normalizedPlanCode,
                PaymentMethod = normalizedPaymentMethod,
                SeatCount = seatCount,
                BaseAmount = Math.Round(baseAmount, 2, MidpointRounding.AwayFromZero),
                DiscountAmount = Math.Round(discountResult.Amount, 2, MidpointRounding.AwayFromZero),
                SupportFee = Math.Round(supportFeeResult.Fee, 2, MidpointRounding.AwayFromZero),
                PaymentFee = Math.Round(paymentFeeResult.Fee, 2, MidpointRounding.AwayFromZero),
                TaxAmount = Math.Round(taxAmount, 2, MidpointRounding.AwayFromZero),
                FinalAmount = Math.Round(finalAmount, 2, MidpointRounding.AwayFromZero),
                Notes = allNotes,
                GeneratedAt = DateTime.UtcNow
            };

            _billingGateway.SaveInvoice(invoice);
            SendNotification(customer, normalizedPlanCode, invoice.FinalAmount);

            return invoice;
        }

        private void ValidateInputs(int customerId, string planCode, int seatCount, string paymentMethod)
        {
            if (customerId <= 0) throw new ArgumentException("Customer id must be positive");
            if (string.IsNullOrWhiteSpace(planCode)) throw new ArgumentException("Plan code is required");
            if (seatCount <= 0) throw new ArgumentException("Seat count must be positive");
            if (string.IsNullOrWhiteSpace(paymentMethod)) throw new ArgumentException("Payment method is required");
        }

        private void SendNotification(Customer customer, string planCode, decimal finalAmount)
        {
            if (string.IsNullOrWhiteSpace(customer.Email)) return;

            string subject = "Subscription renewal invoice";
            string body = $"Hello {customer.FullName}, your renewal for plan {planCode} has been prepared. Final amount: {finalAmount:F2}.";
            _billingGateway.SendEmail(customer.Email, subject, body);
        }
    }
}