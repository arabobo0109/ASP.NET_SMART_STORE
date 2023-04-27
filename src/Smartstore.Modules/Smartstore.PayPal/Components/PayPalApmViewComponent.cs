﻿using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Components;

namespace Smartstore.PayPal.Components
{
    public class PayPalApmViewComponent : SmartViewComponent
    {
        private readonly PayPalSettings _settings;

        public PayPalApmViewComponent(PayPalSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Renders APM (alternative payment method) input fields.
        /// </summary>
        public IViewComponentResult Invoke(string funding)
        {
            // If client id or secret haven't been configured yet, don't render buttons.
            if (!_settings.ClientId.HasValue() || !_settings.Secret.HasValue())
            {
                return Empty();
            }

            var model = new PublicApmModel
            {
                Funding = funding
            };

            // TODO: (mh) (core) Preselect Name & Country from billing address.

            return View(model);
        }
    }
}