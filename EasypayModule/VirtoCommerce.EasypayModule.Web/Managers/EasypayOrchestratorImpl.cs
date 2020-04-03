using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.Domain.Catalog.Services;
using VirtoCommerce.Domain.Commerce.Model;
using VirtoCommerce.Domain.Customer.Services;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Order.Services;
using VirtoCommerce.Domain.Payment.Model;
using VirtoCommerce.Domain.Store.Model;
using VirtoCommerce.Domain.Store.Services;
using VirtoCommerce.Platform.Core.DynamicProperties;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.Easypay.Managers
{
    internal sealed class EasypayOrchestratorImpl : IEasypayOrchestrator
    {
        private readonly ICustomerOrderSearchService _customerOrderSearchService;
        private readonly ICustomerOrderService _customerOrderService;
        private readonly IMemberService _memberService;
        private readonly IStoreService _storeService;
        private readonly IItemService _itemService;
        private readonly ISettingsManager _settingManager;

        private readonly ConcurrentDictionary<string, EasypayClient> _easypayClients;

        public EasypayOrchestratorImpl(ICustomerOrderSearchService customerOrderSearchService, ICustomerOrderService customerOrderService,
            IMemberService memberService, IStoreService storeService, IItemService itemService, ISettingsManager settingManager)
        {
            _customerOrderSearchService = customerOrderSearchService;
            _customerOrderService = customerOrderService;
            _memberService = memberService;
            _storeService = storeService;
            _itemService = itemService;
            _settingManager = settingManager;
            _easypayClients = new ConcurrentDictionary<string, EasypayClient>();
        }

        public Task<CustomerOrder> GetPaymentOrderAsync(string orderCode, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var orderSearchResult = _customerOrderSearchService.SearchCustomerOrders(new CustomerOrderSearchCriteria
                {
                    Number = orderCode,
                    Take = 1,
                    ResponseGroup = CustomerOrderResponseGroup.Full.ToString()
                });
                var customerOrder = orderSearchResult.Results.FirstOrDefault();

                if (customerOrder == null)
                    throw new InvalidOperationException($"Order not found. OrderId: {orderCode}");

                return customerOrder;
            },
            cancellationToken);
        }

        public async Task<int> GetPaymentReferenceAsync(CustomerOrder order, PaymentIn payment, bool splitPayments, CancellationToken cancellationToken)
        {
            var address = order.Addresses.FirstOrDefault(s => (s.AddressType & AddressType.Billing) == AddressType.Billing);

            if (address == null)
                throw new InvalidOperationException($"Order {order.Number} is missing the billing address.");

            EasypayPaymentMethod paymentMethod = payment.PaymentMethod as EasypayPaymentMethod;

            var request = new Model.PaymentRequest
            {
                ClientID = paymentMethod.ClientID,
                Username = paymentMethod.Username,
                EntityID = paymentMethod.EntityID,
                OrderCode = order.Number,
                Value = Math.Round(payment.Sum, 2, MidpointRounding.ToEven),
                Country = paymentMethod.Country,
                CustomerName = String.Join(" ", address.FirstName, address.LastName),
                Email = address.Email
            };

            if (splitPayments)
            {
                var splits = GetPaymentSplitsAsync(order, payment);
                request.AddRange(splits);
            }

            var easypayClient = GetEasypayClient(order.StoreId, paymentMethod.Settings);
            var responseValues = await easypayClient.RequestPaymentIdentifierAsync(request, cancellationToken).ConfigureAwait(false);

            if (responseValues == null || !responseValues.Any(s => s.Key == "ep_reference"))
                throw new InvalidOperationException($"Failed to generate reference for order {order.Number}.");

            return (int)responseValues["ep_reference"];
        }

        public async Task RegisterPaymentAsync(string storeId, int clientId, string username, string transactionId, string type, CancellationToken cancellationToken)
        {
            Store store = null;
            PaymentMethod paymentMethod = null;
            EasypayClient easypayClient;

            if (storeId != null)
            {
                store = _storeService.GetById(storeId);

                if (store != null)
                    throw new InvalidOperationException("Store not found.");

                paymentMethod = store.PaymentMethods.FirstOrDefault(x => x.IsActive && x.Code == "Easypay");

                if (paymentMethod != null)
                    throw new InvalidOperationException($"Easypay payment method not found on store {store.Name}.");

                easypayClient = GetEasypayClient(storeId, paymentMethod.Settings);
            }
            else
            {
                easypayClient = GetEasypayClient(null, null);
            }

            var responseValues = await easypayClient.FetchPaymentDetailAsync(clientId, username, transactionId, type, cancellationToken).ConfigureAwait(false);

            if (!responseValues.Any(s => s.Key == "t_key"))
                throw new InvalidOperationException("Could not retrieve order identifier for payment request.");

            var t_key = (string)responseValues["t_key"];
            var orderSearchResult = _customerOrderSearchService.SearchCustomerOrders(new CustomerOrderSearchCriteria
            {
                Number = t_key,
                Take = 1
            });

            var order = orderSearchResult.Results.FirstOrDefault();

            if (order == null)
                throw new InvalidOperationException($"Order {t_key} not found.");

            if (store == null)
            {
                store = _storeService.GetById(order.StoreId);

                paymentMethod = store.PaymentMethods.FirstOrDefault(x => x.IsActive && x.Code == "Easypay");

                if (paymentMethod == null)
                    throw new InvalidOperationException($"Easypay payment method not found on store {store.Name}.");
            }

            var parameters = new NameValueCollection { { "OrderId", order.Id } };

            foreach (var param in responseValues)
                parameters.Add(param.Key, Convert.ToString(param.Value));

            var validationResult = paymentMethod.ValidatePostProcessRequest(parameters);

            if (!validationResult.IsSuccess)
                throw new InvalidOperationException("Invalid registration parameters.");

            var ep_value = (decimal)responseValues["ep_value"];
            var payment = order.InPayments.FirstOrDefault(x => x.GatewayCode == "Easypay" && Math.Round(x.Sum, 2, MidpointRounding.ToEven) == ep_value);
            
            if (payment == null)
                throw new InvalidOperationException("Order payment operation not found.");

            var context = new PostProcessPaymentEvaluationContext
            {
                Order = order,
                Payment = payment,
                Store = store,
                OuterId = validationResult.OuterId,
                Parameters = parameters
            };

            if (paymentMethod.PostProcessPayment(context).IsSuccess)
            {
                _customerOrderService.SaveChanges(new[] { order });
            }
        }

        public Task RegisterPaymentAsync(string orderCode, int entityId, int reference, decimal value, string transactionId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<Model.PaymentSplit> GetPaymentSplitsAsync(CustomerOrder order, PaymentIn payment)
        {
            if (order.Items.Any(s => !s.Cost.HasValue))
                throw new InvalidOperationException($"Order {order.Number} is missing a cost value.");

            var productIds = order.Items.Select(s => s.ProductId).ToArray();
            var products = _itemService.GetByIds(productIds, VirtoCommerce.Domain.Catalog.Model.ItemResponseGroup.None);

            if (products.Any(s => s.Vendor == null))
                throw new InvalidOperationException($"Order {order.Number} is missing a vender.");

            var vendors = _memberService.GetByIds(products.Select(s => s.Vendor).Distinct().ToArray());
            var settings = _settingManager.GetModuleSettings("Prodto.Easypay");

            var vendorCosts = order.Items.Join(products, item => item.ProductId, product => product.Id, (item, product) => new { product.Vendor, item.ExtendedCostWithTax })
                .GroupBy(vc => vc.Vendor).Select(g => new { Vendor = g.Key, ExtendedCostWithTax = Math.Round(g.Sum(vc => vc.ExtendedCostWithTax.Value), 2, MidpointRounding.ToEven) })
                .ToDictionary(vc => vc.Vendor, vc => vc.ExtendedCostWithTax);

            var splits = new Model.PaymentSplit[]
            {
                new Model.PaymentSplit
                {
                    ClientID = settings.GetSettingValue<int>("Easypay.Account.ClientID", 0),
                    Username = settings.GetSettingValue<string>("Easypay.Account.Username", string.Empty),
                    EntityID = settings.GetSettingValue<int>("Easypay.Account.EntityID", 0),
                    Value = Math.Round(payment.Sum, 2, MidpointRounding.ToEven) - vendorCosts.Sum(vc => vc.Value)
                }
            };
            return splits.Concat(vendors.Select(m => new Model.PaymentSplit
            {
                ClientID = m.GetDynamicPropertyValue(Constants.Easypay_ClientID, 0),
                Username = m.GetDynamicPropertyValue(Constants.Easypay_Username, (string)null),
                EntityID = m.GetDynamicPropertyValue(Constants.Easypay_EntityID, 0),
                Value = vendorCosts.Single(vc => vc.Key == m.Id).Value
            }));
        }

        private EasypayClient GetEasypayClient(string storeId, ICollection<SettingEntry> settings)
        {
            return _easypayClients.GetOrAdd(storeId ?? "*", (key) =>
            {
                if (settings == null)
                {
                    settings = _settingManager.GetModuleSettings("Prodto.Easypay");
                }
                string authenticationKey = settings.GetSettingValue<string>("Easypay.AuthenticationKey", null);
                bool sandbox = settings.GetSettingValue<bool>("Easypay.Sandbox", false);
                return new EasypayClient(authenticationKey, sandbox);
            });
        }
    }
}