using Hangfire;
using Microsoft.Practices.Unity;
using VirtoCommerce.Easypay.BackgroundJobs;
using VirtoCommerce.Easypay.Managers;
using VirtoCommerce.Domain.Customer.Model;
using VirtoCommerce.Domain.Payment.Services;
using VirtoCommerce.Platform.Core.DynamicProperties;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.Easypay
{
    public class Module : ModuleBase
    {
        private readonly IUnityContainer _container;

        public Module(IUnityContainer container)
        {
            _container = container;
        }

        #region IModule Members

        public override void Initialize()
        {
            _container.RegisterType<IEasypayOrchestrator, EasypayOrchestratorImpl>(new ContainerControlledLifetimeManager());
        }

        public override void PostInitialize()
        {
            var paymentMethodsService = _container.Resolve<IPaymentMethodsService>();
            var settingsManager = _container.Resolve<ISettingsManager>();
            var easypayOrchestrator = _container.Resolve<IEasypayOrchestrator>();

            paymentMethodsService.RegisterPaymentMethod(() => new Managers.EasypayPaymentMethod(easypayOrchestrator)
            {
                Name = "Easypay Payments",
                Description = "Easypay payment gateway integration",
                LogoUrl = "Modules/$(Prodto.Easypay)/Content/logo.png",
                Settings = settingsManager.GetModuleSettings("VirtoCommerce.Easypay")
            });

            var cronExpression = settingsManager.GetValue("Easypay.CronExpression", "0/5 * * * *");
            RecurringJob.AddOrUpdate<EasypayScannerJob>("EasypayScannerJob", x => x.Process(), cronExpression);

            RegisterDynamicProperties();
        }

        #endregion

        private void RegisterDynamicProperties()
        {
            var properties = new[]
            {
                new DynamicProperty
                {
                    Id = "Easypay_ClientID",
                    Name = Constants.Easypay_ClientID,
                    ObjectType = typeof(Vendor).FullName,
                    ValueType = DynamicPropertyValueType.Integer,
                    CreatedBy = "Auto"
                },
                new DynamicProperty
                {
                    Id = "Easypay_Username",
                    Name = Constants.Easypay_Username,
                    ObjectType = typeof(Vendor).FullName,
                    ValueType = DynamicPropertyValueType.ShortText,
                    CreatedBy = "Auto"
                },
                new DynamicProperty
                {
                    Id = "Easypay_EntityID",
                    Name = Constants.Easypay_EntityID,
                    ObjectType = typeof(Vendor).FullName,
                    ValueType = DynamicPropertyValueType.Integer,
                    CreatedBy = "Auto"
                }
            };

            var dynamicPropertyService = _container.Resolve<IDynamicPropertyService>();
            dynamicPropertyService.SaveProperties(properties);
        }

        private void UnregisterDynamicProperties()
        {
            var dynamicPropertyService = _container.Resolve<IDynamicPropertyService>();
            dynamicPropertyService.DeleteProperties(new[]
            {
                "Easypay_ClientID", "Easypay_Username", "Easypay_EntityID"
            });
        }
    }
}
