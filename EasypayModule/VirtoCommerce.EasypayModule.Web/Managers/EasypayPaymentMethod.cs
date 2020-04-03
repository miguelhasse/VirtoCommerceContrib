using System;
using System.Collections.Specialized;
using System.Threading;
using VirtoCommerce.Domain.Payment.Model;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.Easypay.Managers
{
	public class EasypayPaymentMethod : PaymentMethod
	{
		private const string ClientStoreSetting = "Easypay.Payment.ClientID";
		private const string UsernameStoreSetting = "Easypay.Payment.Username";
		private const string EntityStoreSetting = "Easypay.Payment.EntityID";
        private const string CountryStoreSetting = "Easypay.Country";
        private const string SplitPaymentsSetting = "Easypay.SplitPayments";

        private readonly Managers.IEasypayOrchestrator _easypayProcessor;

        public EasypayPaymentMethod(Managers.IEasypayOrchestrator easypayProcessor) : base("Easypay")
		{
            _easypayProcessor = easypayProcessor;
		}

		public override PaymentMethodType PaymentMethodType
		{
			get { return PaymentMethodType.PreparedForm; }
		}

		public override PaymentMethodGroupType PaymentMethodGroupType
		{
			get { return PaymentMethodGroupType.Manual; }
		}

        internal int ClientID
		{
			get { return Settings.GetSettingValue<int>(ClientStoreSetting, 0); }
		}

        internal string Username
		{
			get { return GetSetting(UsernameStoreSetting); }
		}

        internal int EntityID
		{
			get { return Settings.GetSettingValue<int>(EntityStoreSetting, 0); }
		}

        internal string Country
        {
            get { return GetSetting(CountryStoreSetting); }
        }

        internal bool SplitPayments
        {
            get { return Settings.GetSettingValue<bool>(SplitPaymentsSetting, false); }
        }

        public override ProcessPaymentResult ProcessPayment(ProcessPaymentEvaluationContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			if (context.Payment == null)
				throw new ArgumentNullException(nameof(context.Payment));

			if (context.Order == null)
				throw new ArgumentNullException(nameof(context.Order));

            int reference;
            if (context.Payment.PaymentStatus == PaymentStatus.New)
            {
                try
                {
                    reference = _easypayProcessor.GetPaymentReferenceAsync(context.Order, context.Payment, SplitPayments, CancellationToken.None).Result;
                }
                catch (Exception ex)
                {
                    return new ProcessPaymentResult
                    {
                        Error = ex.GetBaseException().Message,
                        IsSuccess = false
                    };
                }

                context.Payment.OuterId = Convert.ToString(reference);
                context.Payment.PaymentStatus = PaymentStatus.Pending;
            }

            var fb = new System.Text.StringBuilder("<form method=\"POST\"><table class=\"easypay\">");
            fb.Append($"<tr><td class=\"easypay-label-entity\"></td><td class=\"easypay-value\">{EntityID}</td></tr>");

            if (Int32.TryParse(context.Payment.OuterId, out reference))
                fb.Append($"<tr><td class=\"easypay-label-reference\"></td><td class=\"easypay-value\">{reference:000'\xA0'000'\xA0'000}</td></tr>");

            fb.Append($"<tr><td class=\"easypay-label-value\"></td><td class=\"easypay-value\">{context.Payment.Sum:N2}</td></tr>");
            fb.Append("</table></form>");

            return new ProcessPaymentResult
			{
                HtmlForm = fb.ToString(),
                OuterId = context.Payment.OuterId,
                NewPaymentStatus = context.Payment.PaymentStatus,
				IsSuccess = true
			};
		}

		public override PostProcessPaymentResult PostProcessPayment(PostProcessPaymentEvaluationContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			if (context.Payment == null)
				throw new ArgumentNullException(nameof(context.Payment));

			if (context.Parameters == null)
				throw new ArgumentNullException(nameof(context.Parameters));

			if (context.Payment.PaymentStatus != PaymentStatus.Pending)
				throw new InvalidOperationException($"Post process payment failed: payment status is {context.Payment.PaymentStatus}");

            context.Payment.OuterId = context.OuterId; // transaction identifier
            context.Payment.PaymentStatus = PaymentStatus.Paid;
            context.Payment.AuthorizedDate = DateTime.UtcNow;
            context.Payment.IsApproved = true;

			return new PostProcessPaymentResult
			{
				OrderId = context.Order.Id,
				OuterId = context.Payment.OuterId,
				NewPaymentStatus = context.Payment.PaymentStatus,
				IsSuccess = true
			};
		}

		public override CaptureProcessPaymentResult CaptureProcessPayment(CaptureProcessPaymentEvaluationContext context)
		{
			throw new NotImplementedException();
		}

        public override VoidProcessPaymentResult VoidProcessPayment(VoidProcessPaymentEvaluationContext context)
        {
            throw new NotImplementedException();
        }

        public override RefundProcessPaymentResult RefundProcessPayment(RefundProcessPaymentEvaluationContext context)
		{
			throw new NotImplementedException();
		}

		public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
		{
			if (queryString == null)
				throw new ArgumentNullException(nameof(queryString));

			var transactionId = queryString["ep_doc"];

			return new ValidatePostProcessRequestResult
			{
				OuterId = transactionId,
				IsSuccess = !string.IsNullOrEmpty(transactionId)
			};
		}
	}
}