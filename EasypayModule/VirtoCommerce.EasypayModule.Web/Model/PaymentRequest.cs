using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.Easypay.Model
{
    internal sealed class PaymentRequest : List<PaymentSplit>
    {
        public int ClientID { get; set; }

        public string Username { get; set; }

        public int EntityID { get; set; }

        public string OrderCode { get; set; }

        public decimal Value { get; set; }

        public string Country { get; set; }

        public string Language { get; set; }

        public string CustomerName { get; set; }

        public string Email { get; set; }

        public DateTime? Expiration { get; set; }

        public IDictionary<string, object> CreateParameters()
        {
            var parameters = new Dictionary<string, object>
            {
                { "ep_cin", ClientID }, { "ep_user", Username }, { "ep_entity", EntityID }, { "ep_country", Country },
                { "t_value", Value }, { "t_key", OrderCode }, { "ep_ref_type", "auto" },
                { "ep_language", Language }, { "o_name", CustomerName }, { "o_email", Email }, { "o_max_date", Expiration }
            };
            if (Count > 0)
            {
                parameters.Add("ret_type", "xml");
                parameters.Add("ep_split", "normal");
                var json_splits = this.Select((s, n) => FormattableString.Invariant($"\"{n}\":{{\"ep_user\":\"{s.Username}\",\"ep_partner\":\"{Username}\",\"ep_cin\":\"{s.ClientID}\",\"ep_entity\":\"{s.EntityID}\",\"ep_country\":\"{Country}\",\"t_value\":\"{s.Value:F2}\",\"t_value_type\":\"fixed\"}}"));
                parameters.Add("split_json", $"{{\"split_payment\":{{{String.Join(",", json_splits)}}}}}");
            }
            return parameters;
        }
    }
}
