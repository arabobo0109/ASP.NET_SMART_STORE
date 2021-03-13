using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common.Settings;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Core.Stores;
using Smartstore.Engine.Modularity;

namespace Smartstore.Core.Common.Services
{
    public partial class CurrencyService : ICurrencyService
    {
        private readonly SmartDbContext _db;
        private readonly ILocalizationService _localizationService;
        private readonly IProviderManager _providerManager;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly CurrencySettings _currencySettings;
        private readonly TaxSettings _taxSettings;
        private readonly ISettingFactory _settingFactory;

        private Currency _primaryCurrency;
        private Currency _primaryExchangeCurrency;

        public CurrencyService(
            SmartDbContext db,
            ILocalizationService localizationService,
            IProviderManager providerManager,
            IWorkContext workContext,
            IStoreContext storeContext,
            CurrencySettings currencySettings,
            TaxSettings taxSettings,
            ISettingFactory settingFactory)
        {
            _db = db;
            _localizationService = localizationService;
            _providerManager = providerManager;
            _workContext = workContext;
            _storeContext = storeContext;
            _currencySettings = currencySettings;
            _taxSettings = taxSettings;
            _settingFactory = settingFactory;
        }

        public virtual Currency PrimaryCurrency
        {
            get => _primaryCurrency ??= GetPrimaryCurrency(false);
            set => _primaryCurrency = value;
        }

        public virtual Currency PrimaryExchangeCurrency
        {
            get => _primaryExchangeCurrency ??= GetPrimaryCurrency(true);
            set => _primaryExchangeCurrency = value;
        }

        private Currency GetPrimaryCurrency(bool forExchange)
        {
            var currencyId = forExchange ? _currencySettings.PrimaryExchangeCurrencyId : _currencySettings.PrimaryCurrencyId;
            var currency = _db.Currencies.FindById(currencyId, false);

            if (currency == null)
            {
                var allCurrencies = _db.Currencies.AsNoTracking().ToList();
                currency = 
                    allCurrencies.FirstOrDefault(x => x.Published) ??
                    allCurrencies.FirstOrDefault() ??
                    throw new InvalidOperationException("Unable to load primary currency.");

                if (forExchange)
                {
                    _currencySettings.PrimaryExchangeCurrencyId = currency.Id;
                }
                else
                {
                    _currencySettings.PrimaryCurrencyId = currency.Id;
                }

                _settingFactory.SaveSettingsAsync(_currencySettings).Await();
            }

            return currency;
        }

        #region Currency conversion

        public virtual Money ConvertToWorkingCurrency(Money amount)
        {
            Guard.NotNull(amount.Currency, nameof(amount.Currency));
            return amount.ExchangeTo(_workContext.WorkingCurrency, PrimaryExchangeCurrency);
        }

        public virtual Money ConvertToWorkingCurrency(decimal amount)
        {
            return new Money(amount, PrimaryCurrency).ExchangeTo(_workContext.WorkingCurrency, PrimaryExchangeCurrency);
        }

        #endregion

        #region Exchange rate provider

        public virtual Task<IList<ExchangeRate>> GetCurrencyLiveRatesAsync(string exchangeRateCurrencyCode)
        {
            var exchangeRateProvider = LoadActiveExchangeRateProvider();
            if (exchangeRateProvider != null)
            {
                return exchangeRateProvider.Value.GetCurrencyLiveRatesAsync(exchangeRateCurrencyCode);
            }
            else
            {
                return Task.FromResult<IList<ExchangeRate>>(new List<ExchangeRate>());
            }
        }

        public virtual Provider<IExchangeRateProvider> LoadActiveExchangeRateProvider()
        {
            return LoadExchangeRateProviderBySystemName(_currencySettings.ActiveExchangeRateProviderSystemName) ?? LoadAllExchangeRateProviders().FirstOrDefault();
        }

        public virtual Provider<IExchangeRateProvider> LoadExchangeRateProviderBySystemName(string systemName)
        {
            return _providerManager.GetProvider<IExchangeRateProvider>(systemName);
        }

        public virtual IEnumerable<Provider<IExchangeRateProvider>> LoadAllExchangeRateProviders()
        {
            return _providerManager.GetAllProviders<IExchangeRateProvider>();
        }

        #endregion

        public virtual Money CreateMoney(decimal price, bool displayCurrency = true, object currencyCodeOrObj = null)
        {
            Currency currency = null;

            if (currencyCodeOrObj is null)
            {
                currency = _workContext.WorkingCurrency;
            }
            else if (currencyCodeOrObj is string currencyCode)
            {
                Guard.NotEmpty(currencyCode, nameof(currencyCodeOrObj));
                currency =
                    (currencyCode == PrimaryCurrency.CurrencyCode ? PrimaryCurrency : null) ??
                    (currencyCode == PrimaryExchangeCurrency.CurrencyCode ? PrimaryExchangeCurrency : null) ??
                    _db.Currencies.FirstOrDefault(x => x.CurrencyCode == currencyCode) ?? 
                    new Currency { CurrencyCode = currencyCode };
            }
            else if (currencyCodeOrObj is Currency)
            {
                currency = (Currency)currencyCodeOrObj;
            }

            if (currency == null)
            {
                throw new ArgumentException("Currency parameter must either be a valid currency code as string or an actual currency entity instance.", nameof(currencyCodeOrObj));
            }

            return new Money(price, currency, !displayCurrency);
        }

        public virtual string GetTaxFormat(
            bool? displayTaxSuffix = null,
            bool? priceIncludesTax = null,
            PricingTarget target = PricingTarget.Product,
            Language language = null)
        {
            // TODO: (core) Does GetTaxFormat belong to ITaxService? Hmmm... (?)

            displayTaxSuffix ??= target == PricingTarget.Product
                ? _taxSettings.DisplayTaxSuffix
                : (target == PricingTarget.ShippingCharge
                    ? _taxSettings.DisplayTaxSuffix && _taxSettings.ShippingIsTaxable
                    : _taxSettings.DisplayTaxSuffix && _taxSettings.PaymentMethodAdditionalFeeIsTaxable);

            if (displayTaxSuffix == true)
            {
                // Show tax suffix.
                priceIncludesTax ??= _workContext.TaxDisplayType == TaxDisplayType.IncludingTax;
                language ??= _workContext.WorkingLanguage;

                string resource = _localizationService.GetResource(priceIncludesTax.Value ? "Products.InclTaxSuffix" : "Products.ExclTaxSuffix", language.Id, false);
                var postFormat = resource.NullEmpty() ?? (priceIncludesTax.Value ? "{0} incl. tax" : "{0} excl. tax");

                return postFormat;
            }
            else
            {
                return null;
            }
        }

        public virtual Money ApplyTaxFormat(
            Money source,
            bool? displayTaxSuffix = null, 
            bool ? priceIncludesTax = null, 
            PricingTarget target = PricingTarget.Product, 
            Language language = null)
        {
            // TODO: (core) Does ApplyTaxFormat belong to ITaxService? Hmmm... (?)

            if (source == 0)
                return source;

            displayTaxSuffix ??= target == PricingTarget.Product
                ? _taxSettings.DisplayTaxSuffix
                : (target == PricingTarget.ShippingCharge
                    ? _taxSettings.DisplayTaxSuffix && _taxSettings.ShippingIsTaxable
                    : _taxSettings.DisplayTaxSuffix && _taxSettings.PaymentMethodAdditionalFeeIsTaxable);

            if (displayTaxSuffix == true)
            {
                // Show tax suffix.
                priceIncludesTax ??= _workContext.TaxDisplayType == TaxDisplayType.IncludingTax;
                language ??= _workContext.WorkingLanguage;

                string resource = _localizationService.GetResource(priceIncludesTax.Value ? "Products.InclTaxSuffix" : "Products.ExclTaxSuffix", language.Id, false);
                var postFormat = resource.NullEmpty() ?? (priceIncludesTax.Value ? "{0} incl. tax" : "{0} excl. tax");

                return source.WithPostFormat(postFormat);
            }
            else
            {
                return source;
            }
        }
    }
}