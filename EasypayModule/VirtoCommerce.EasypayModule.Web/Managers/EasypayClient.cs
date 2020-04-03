using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace VirtoCommerce.Easypay.Managers
{
    internal sealed class EasypayClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly string authenticationKey;
        private bool disposed = false;

        static readonly string[] DateTimeFormats = new string[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };

        #region Constructors

	
        public EasypayClient(string authenticationKey, bool sandbox = false)
        {
            this.authenticationKey = authenticationKey ?? throw new ArgumentNullException(nameof(authenticationKey));

            var clientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                PreAuthenticate = true,
                UseDefaultCredentials = true,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip
            };

            this.client = new HttpClient(clientHandler)
            {
                BaseAddress = new Uri(sandbox ? "http://test.easypay.pt/_s/" : "https://www.easypay.pt/_s/")
            };
        }

        #endregion

        #region Disposal Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing) // dispose aggregated resources
                    this.client.Dispose();
                this.disposed = true; // disposing has been done
            }
        }

        #endregion

        #region Request Handling Methods

        private Task<XElement> GetAsync(string cmd, IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            if (parameters == null) parameters = new Dictionary<string, object>();
            TaskCompletionSource<XElement> tcs = new TaskCompletionSource<XElement>();
            this.client.GetAsync(CreateRequestUri(cmd, parameters), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ContinueWith(t => HandleResponseCompletion(t, tcs));
            return tcs.Task;
        }

        private Task<XElement> PostAsync(string cmd, IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var nameValueCollection = new Dictionary<string, string> { { "s_code", authenticationKey } };
            TaskCompletionSource<XElement> tcs = new TaskCompletionSource<XElement>();

            if (parameters != null)
            {
                foreach (var p in parameters)
				{
                    if (p.Value != null)
                    {
                        nameValueCollection.Add(p.Key, ConvertParameterValue(p.Value));
                    }
				}
            }
            var content = new FormUrlEncodedContent(nameValueCollection);
            this.client.PostAsync($"api_easypay_{cmd}.php", content, cancellationToken)
                .ContinueWith(t => HandleResponseCompletion(t, tcs));
            return tcs.Task;
        }

        private static void HandleResponseCompletion(Task<HttpResponseMessage> task, TaskCompletionSource<XElement> tcs)
        {
            if (task.IsCanceled)
            {
                System.Diagnostics.Trace.TraceError("Easypay client request canceled.");
                tcs.SetCanceled();
            }
            else if (task.IsFaulted)
            {
                var exception = task.Exception.GetBaseException();
                exception = exception.InnerException ?? exception;

                System.Diagnostics.Trace.TraceError("Easypay client request exception: {0}", exception.Message);
                tcs.SetException(exception);
            }
            else if (task.IsCompleted)
            {
                try
                {
                    task.Result.EnsureSuccessStatusCode();
                    var root = XElement.Load(task.Result.Content.ReadAsStreamAsync().Result);

                    var element = root.Element("ep_status");
                    if (element != null || element.Value == null)
                    {
                        if (!element.Value.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                        {
                            element = root.Element("ep_message");
                            System.Diagnostics.Trace.TraceError("Easypay service error: {0}", element.Value);
                            tcs.SetException(new Exception(element.Value));
                        }
                        else tcs.SetResult(root);
                    }
                    else tcs.SetException(new Exception("Response is missing ep_status"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Easypay client request exception: {0}", ex.Message);
                    tcs.SetException(ex);
                }
            }
        }

        private string CreateRequestUri(string cmd, IDictionary<string, object> parameters)
        {
            if (parameters != null)
            {
				parameters.Add("s_code", authenticationKey);
                string query = String.Join("&", parameters.Where(s => s.Value != null)
                    .Select(s => String.Concat(s.Key, "=", ConvertParameterValue(s.Value))));

                return $"api_easypay_{cmd}.php?{query}";
            }
            return $"api_easypay_{cmd}.php?s_code={authenticationKey}";
        }

        private static string ConvertParameterValue(object value)
        {
            Type t = value.GetType();
            t = Nullable.GetUnderlyingType(t) ?? t;

            if (t == typeof(DateTime)) return ((DateTime)value).ToString(DateTimeFormats[0]);
            else if (t == typeof(Decimal)) return ((Decimal)value).ToString("F2", CultureInfo.InvariantCulture);
            else return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        #endregion

        public async Task<IDictionary<string, object>> FetchPaymentDetailAsync(int clientId, string username, string transactionId, string type, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object>
            {
                { "ep_cin", clientId }, { "ep_user", username }, { "ep_doc", transactionId }, { "ep_type", type }
            };
            return ToDictionary(await GetAsync("03AG", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<IEnumerable<IDictionary<string, object>>> FetchPaymentsAsync(int clientId, string username, int entityId, DateTime start, DateTime end, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object>
            {
                { "ep_cin", clientId }, { "ep_user", username }, { "ep_entity", entityId },
                { "o_list_type", "date" }, { "o_ini", start }, { "o_last", end }
            };
            return ToDictionaryCollection(await GetAsync("040BG1", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<IEnumerable<IDictionary<string, object>>> FetchFailedPaymentsAsync(int clientId, string username, int entityId, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object>
            {
                { "ep_cin", clientId }, { "ep_user", username }, { "ep_entity", entityId }, { "o_list_type", "fail" }
            };
            return ToDictionaryCollection(await GetAsync("040BG1", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<IDictionary<string, object>> RequestPaymentIdentifierAsync(Model.PaymentRequest request, CancellationToken cancellationToken)
        {
            var parameters = request.CreateParameters();
            if (parameters.ContainsKey("ep_split"))
            {
                return ToDictionary(await PostAsync("01SP", parameters, cancellationToken).ConfigureAwait(false));
            }
            return ToDictionary(await GetAsync("01BG", parameters, cancellationToken).ConfigureAwait(false));
        }

        private static IEnumerable<IDictionary<string, object>> ToDictionaryCollection(XElement root)
        {
            return root.Descendants("ref").Select(element => ToDictionary(element));
        }

        private static IDictionary<string, object> ToDictionary(XElement root)
        {
            return root.Elements().Select(element =>
            {
                switch (element.Name.LocalName)
                {
                    case "ep_cin":
                    case "ep_entity":
                    case "ep_reference":
                        return new KeyValuePair<string, object>(element.Name.LocalName,
                            Int32.Parse(element.Value, CultureInfo.InvariantCulture));
                    case "ep_value_fixed":
                    case "ep_value_var":
                    case "ep_value_tax":
                    case "ep_value_transf":
                    case "ep_value":
                        return new KeyValuePair<string, object>(element.Name.LocalName,
                            Decimal.Parse(element.Value, CultureInfo.InvariantCulture));
                    case "ep_date":
                    case "ep_date_read":
                    case "ep_date_transf":
                        return new KeyValuePair<string, object>(element.Name.LocalName,
                            XmlConvert.ToDateTimeOffset(element.Value, DateTimeFormats).DateTime.ToUniversalTime());
                    default:
                        return new KeyValuePair<string, object>(element.Name.LocalName, element.Value);
                }
            })
            .ToDictionary(s => s.Key, s => s.Value);
        }
    }
}
