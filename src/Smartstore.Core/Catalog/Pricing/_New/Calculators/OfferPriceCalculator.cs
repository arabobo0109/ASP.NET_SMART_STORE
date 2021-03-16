﻿using System;
using System.Threading.Tasks;
using Smartstore.Core.Catalog.Products;

namespace Smartstore.Core.Catalog.Pricing.Calculators
{
    public class OfferPriceCalculator : IPriceCalculator
    {
        public async Task CalculateAsync(CalculatorContext context, CalculatorDelegate next)
        {
            if (context.Product is Product product && product.SpecialPrice.HasValue)
            {
                // Check date range
                var now = DateTime.UtcNow;
                var from = product.SpecialPriceStartDateTimeUtc;
                var to = product.SpecialPriceEndDateTimeUtc;

                if ((from == null || now >= from) && (to == null || now <= to))
                {
                    context.OfferPrice = product.SpecialPrice;
                    context.FinalPrice = product.SpecialPrice.Value;
                }
            }
            
            await next(context);
        }
    }
}
