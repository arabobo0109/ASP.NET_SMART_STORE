﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Domain.Catalog;
using Smartstore.Web.Controllers;

namespace Smartstore.Web.Components
{
    public class HomeBrandsViewComponent : SmartViewComponent
    {
        private readonly CatalogHelper _catalogHelper;
        private readonly CatalogSettings _catalogSettings;

        public HomeBrandsViewComponent(CatalogHelper catalogHelper, CatalogSettings catalogSettings)
        {
            _catalogHelper = catalogHelper;
            _catalogSettings = catalogSettings;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (_catalogSettings.ManufacturerItemsToDisplayOnHomepage > 0 && _catalogSettings.ShowManufacturersOnHomepage)
            {
                var model = await _catalogHelper.PrepareBrandNavigationModelAsync(_catalogSettings.ManufacturerItemsToDisplayOnHomepage);
                if (model.Brands.Any())
                {
                    return View(model);
                }
            }

            return Empty();
        }
    }
}
