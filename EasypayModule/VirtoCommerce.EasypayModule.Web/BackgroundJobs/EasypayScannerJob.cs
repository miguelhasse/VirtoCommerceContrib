using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using VirtoCommerce.Domain.Commerce.Model;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Order.Services;
using VirtoCommerce.Domain.Payment.Model;
using VirtoCommerce.Domain.Store.Services;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.Easypay.BackgroundJobs
{
    public sealed class EasypayScannerJob
    {
        private readonly Managers.IEasypayOrchestrator _easypayProcessor;

        public EasypayScannerJob(Managers.IEasypayOrchestrator easypayProcessor)
        {
            _easypayProcessor = easypayProcessor;
        }

        public void Process()
        {

        }
    }
}