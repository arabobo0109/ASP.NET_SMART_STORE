﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Domain.Catalog;
using Smartstore.Core.Localization;
using Smartstore.Web.Modelling;
using Smartstore.Web.Models.Common;
using Smartstore.Web.Models.Media;

namespace Smartstore.Web.Models.Catalog
{
    // TODO: (mh) (core) Implement ISearchResultModel later.
    public partial class CategoryModel : EntityModelBase//, ISearchResultModel
    {
        public CatalogSearchResult SearchResult
        {
            get;
            set;
        }

        public LocalizedValue<string> Name { get; set; }
        public LocalizedValue<string> FullName { get; set; }
        public LocalizedValue<string> Description { get; set; }
        public LocalizedValue<string> BottomDescription { get; set; }
        public LocalizedValue<string> MetaKeywords { get; set; }
        public LocalizedValue<string> MetaDescription { get; set; }
        public LocalizedValue<string> MetaTitle { get; set; }

        public string SeName { get; set; }
        public string CanonicalUrl { get; set; }
        public bool DisplayCategoryBreadcrumb { get; set; }

        public ImageModel Image { get; set; } = new();

        public SubCategoryDisplayType SubCategoryDisplayType { get; set; }
        public IList<CategorySummaryModel> SubCategories { get; set; } = new List<CategorySummaryModel>();
        public ProductSummaryModel FeaturedProducts { get; set; }
        public ProductSummaryModel Products { get; set; }
        public MetaPropertiesModel MetaProperties { get; set; } = new();
    }
}
