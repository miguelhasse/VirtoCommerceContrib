namespace VirtoCommerce.Easypay.Model
{
    internal sealed class PaymentSplit
    {
        public int ClientID { get; set; }

        public string Username { get; set; }

        public int EntityID { get; set; }

        public decimal Value { get; set; }
    }
}