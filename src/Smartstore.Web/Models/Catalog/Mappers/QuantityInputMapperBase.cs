﻿using Microsoft.AspNetCore.Mvc.Rendering;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Products;

namespace Smartstore.Web.Models.Catalog.Mappers
{
    public abstract class QuantityInputMapperBase<TFrom, TTo> : IMapper<TFrom, TTo>
        where TFrom : class
        where TTo : class, IQuantityInput
    {
        public async Task MapAsync(TFrom from, TTo to, dynamic parameters = null)
        {
            await MapCoreAsync(from, to, parameters);

            // Determine the best control for the given settings (spinner or dropdown)
            PostProcess(to);
        }

        protected abstract Task MapCoreAsync(TFrom source, TTo model, dynamic parameters = null);

        protected void MapCustomQuantities(TTo model, int[] customQuantities)
        {
            if (!customQuantities.IsNullOrEmpty())
            {
                Array.Sort(customQuantities);

                model.AllowedQuantities.AddRange(customQuantities.Select(qty => new SelectListItem
                {
                    Text = qty.ToString(),
                    Value = qty.ToStringInvariant(),
                    Selected = model.EnteredQuantity == qty
                }));
            } 
        }

        protected virtual void PostProcess(TTo model)
        {
            // A custom quantity list is best displayed as dropdown
            var inputType = QuantityControlType.Dropdown;

            if (model.AllowedQuantities.Count == 0)
            {
                if (model.MinOrderAmount < 1)
                {
                    model.MinOrderAmount = 1;
                }

                if (model.MaxOrderAmount < model.MinOrderAmount)
                {
                    model.MaxOrderAmount = model.MinOrderAmount;
                }

                if (model.QuantityStep < 1)
                {
                    model.QuantityStep = 1;
                }

                if (model.QuantityStep > model.MaxOrderAmount)
                {
                    model.QuantityStep = model.MaxOrderAmount;
                }

                var min = model.MinOrderAmount;
                var max = model.MaxOrderAmount;
                var step = model.QuantityStep;
                var range = Math.Max(1, max - min);

                if (range / step > 100)
                {
                    // Dropdown should not display more than 100+ quantity options.
                    inputType = QuantityControlType.Spinner;
                }
                else
                {
                    // Less than 100 quantities, prepare select options.
                    var options = new List<SelectListItem>();

                    for (var i = min; i <= max; i += step)
                    {
                        options.Add(new SelectListItem { Text = i.ToString(), Value = i.ToStringInvariant(), Selected = i == model.EnteredQuantity });
                    }

                    model.AllowedQuantities.AddRange(options);
                }
            }

            model.QuantityControlType = inputType;
        }
    }
}
