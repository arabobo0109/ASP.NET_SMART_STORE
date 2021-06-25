﻿using System.Collections.Generic;
using Smartstore.Web.Modelling;

namespace Smartstore.Admin.Models.Orders
{
    public class DashboardBestsellersModel : ModelBase
    {
        public IList<BestsellersReportLineModel> BestsellersByQuantity { get; set; }
        public IList<BestsellersReportLineModel> BestsellersByAmount { get; set; }
    }
}
