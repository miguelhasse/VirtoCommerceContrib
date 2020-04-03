using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using System.Web.Http.Filters;
using System.Xml.Linq;
using VirtoCommerce.Domain.Commerce.Model;

namespace VirtoCommerce.Easypay.Controllers
{
    [XmlException]
    [ApiExplorerSettings(IgnoreApi = true)]
    [RoutePrefix("api/payments/easypay")]
    public class EasypayController : ApiController
    {
        private readonly Managers.IEasypayOrchestrator _easypayProcessor;

        public EasypayController(Managers.IEasypayOrchestrator easypayProcessor)
        {
            _easypayProcessor = easypayProcessor;
        }

        [HttpGet]
        [Route("register")]
        [AllowAnonymous]
        public Task<IHttpActionResult> RegisterPayment(int ep_cin, string ep_user, string ep_doc, string ep_type = null)
        {
            return RegisterPayment(null, ep_cin, ep_user, ep_doc, ep_type);
        }

        [HttpGet]
        [Route("register/{store}")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> RegisterPayment(string store, int ep_cin, string ep_user, string ep_doc, string ep_type = null)
        {
            await _easypayProcessor.RegisterPaymentAsync(store, ep_cin, ep_user, ep_doc, ep_type, CancellationToken.None);

            var response = new XElement("getautomb_key",
                new XElement("ep_status", "ok"),
                new XElement("ep_message", "Payment registration accepted"),
                new XElement("ep_cin", ep_cin),
                new XElement("ep_user", ep_user),
                new XElement("ep_doc", ep_doc),
                new XElement("ep_type", ep_type));

            return ResponseMessage(BuildResponseMessage(HttpStatusCode.OK, response));
        }

        [HttpGet]
        [Route("detail")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> OrderDetail(int e, int r, decimal v, string t_key)
        {
            var customerOrder = await _easypayProcessor.GetPaymentOrderAsync(t_key, CancellationToken.None);

            var orderinfo = new XElement("order_info",
                new XElement("total_taxes", customerOrder.TaxTotal),
                new XElement("total_including_taxes", customerOrder.Total));

            var billingAddress = customerOrder.Addresses
                .FirstOrDefault(address => (address.AddressType & AddressType.Billing) == AddressType.Billing);

            if (billingAddress != null)
            {
                orderinfo.Add(new XElement("bill_name", billingAddress.Name),
                    new XElement("bill_address_1", billingAddress.Line1),
                    new XElement("bill_address_2", billingAddress.Line2),
                    new XElement("bill_city", billingAddress.City),
                    new XElement("bill_zip_code", billingAddress.PostalCode),
                    new XElement("bill_country", billingAddress.CountryName));
            }

            var shippingAddress = customerOrder.Addresses
                .FirstOrDefault(address => (address.AddressType & AddressType.Shipping) == AddressType.Shipping);

            if (shippingAddress != null)
            {
                orderinfo.Add(new XElement("shipp_name", billingAddress.Name),
                    new XElement("shipp_address_1", billingAddress.Line1),
                    new XElement("shipp_address_2", billingAddress.Line2),
                    new XElement("shipp_city", billingAddress.City),
                    new XElement("shipp_zip_code", billingAddress.PostalCode),
                    new XElement("shipp_country", billingAddress.CountryName));
            }

            var orderdetail = customerOrder.Items.Select(item =>
                new XElement("order_info",
                    new XElement("item_description", item.Name),
                    new XElement("item_quantity", item.Quantity),
                    new XElement("item_total", item.PriceWithTax)));

            var response = new XElement("get_detail",
                new XElement("ep_status", "ok"),
                new XElement("ep_message", "generated document"),
                new XElement("ep_entity", e),
                new XElement("ep_reference", r),
                new XElement("ep_value", v),
                new XElement("t_key", t_key),
                orderinfo, orderdetail);

            return ResponseMessage(BuildResponseMessage(HttpStatusCode.OK, response));
        }

        internal static HttpResponseMessage BuildResponseMessage(HttpStatusCode statusCode, XElement response)
        {
            var xdoc = new XDocument(
                new XDeclaration("1.0", "ISO-8859-1", String.Empty),
                new XElement(response));

            var encoding = System.Text.Encoding.GetEncoding(xdoc.Declaration.Encoding);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(xdoc.ToString(), encoding, "application/xml")
            };
        }
    }

    internal sealed class XmlExceptionAttribute : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext context)
        {
            if (context.Exception != null)
            {
                var response = new XElement("details",
                    new XElement("ep_status", "err"),
                    new XElement("ep_message", context.Exception.Message));

                context.Response = EasypayController.BuildResponseMessage(HttpStatusCode.InternalServerError, response);
            }
        }
    }
}