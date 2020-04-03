using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.Domain.Order.Model;

namespace VirtoCommerce.Easypay.Managers
{
    public interface IEasypayOrchestrator
    {
        Task RegisterPaymentAsync(string storeId, int clientId, string username, string transactionId, string type, CancellationToken cancellationToken);

        Task RegisterPaymentAsync(string orderCode, int entityId, int reference, decimal value, string transactionId, CancellationToken cancellationToken);

        Task<CustomerOrder> GetPaymentOrderAsync(string orderCode, CancellationToken cancellationToken);

        Task<int> GetPaymentReferenceAsync(CustomerOrder order, PaymentIn payment, bool splitPayments, CancellationToken cancellationToken);
    }
}