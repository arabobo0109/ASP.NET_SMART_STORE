﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Collections;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Data;

namespace Smartstore.Core.Catalog.Products
{
    public partial class ProductService : IProductService
    {
        private readonly SmartDbContext _db;

        public ProductService(
            SmartDbContext db)
        {
            _db = db;
        }

        public virtual async Task<(Product Product, ProductVariantAttributeCombination VariantCombination)> GetProductByIdentificationNumberAsync(
            string identificationNumber,
            bool includeHidden = false,
            bool tracked = false)
        {
            if (string.IsNullOrWhiteSpace(identificationNumber))
            {
                return (null, null);
            }

            identificationNumber = identificationNumber.Trim();

            var pq = _db.Products
                .ApplyTracking(tracked)
                .ApplyStandardFilter(includeHidden)
                .Where(x => x.Sku == identificationNumber || x.ManufacturerPartNumber == identificationNumber || x.Gtin == identificationNumber);

            if (!includeHidden)
            {
                pq = pq.Where(x => x.Visibility <= ProductVisibility.SearchResults);
            }

            var product = await pq.FirstOrDefaultAsync();
            if (product != null)
            {
                return (product, null);
            }

            var pvcq = _db.ProductVariantAttributeCombinations
                .Include(x => x.Product)
                .ApplyTracking(tracked)
                .ApplyStandardFilter(includeHidden)
                .Where(x => x.Sku == identificationNumber || x.ManufacturerPartNumber == identificationNumber || x.Gtin == identificationNumber);

            if (!includeHidden)
            {
                pvcq = pvcq.Where(x => x.Product.Visibility <= ProductVisibility.SearchResults);
            }

            var variantCombination = await pvcq.FirstOrDefaultAsync();

            return (variantCombination?.Product, variantCombination);
        }

        public virtual async Task<IList<Product>> GetLowStockProductsAsync(bool tracked = false)
        {
            var query = _db.Products
                .ApplyTracking(tracked)
                .ApplyStandardFilter(true);

            // Track inventory for product.
            var query1 = 
                from p in query
                orderby p.MinStockQuantity
                where p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock && p.MinStockQuantity >= p.StockQuantity
                select p;

            var products1 = await query1.ToListAsync();

            // Track inventory for product by product attributes.
            var query2 = 
                from p in query
                from pvac in p.ProductVariantAttributeCombinations
                where p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStockByAttributes && pvac.StockQuantity <= 0
                select p;

            var products2 = await query2.ToListAsync();

            var result = new List<Product>();
            result.AddRange(products1);
            result.AddRange(products2);

            return result;
        }

        public virtual async Task<Multimap<int, ProductTag>> GetProductTagsByProductIdsAsync(int[] productIds, bool includeHidden = false)
        {
            Guard.NotNull(productIds, nameof(productIds));

            var map = new Multimap<int, ProductTag>();
            if (!productIds.Any())
            {
                return map;
            }

            var query = _db.Products
                .AsNoTracking()
                .Include(x => x.ProductTags)
                .Where(x => productIds.Contains(x.Id))
                .ApplyStandardFilter(includeHidden);

            if (!includeHidden)
            {
                // Only tags of products that are fully visible.
                query = query.Where(x => x.Visibility == ProductVisibility.Full);
            }

            var items = await query
                .Select(x => new
                {
                    ProductId = x.Id,
                    Tags = x.ProductTags.Where(y => includeHidden || y.Published)
                })
                .ToListAsync();

            foreach (var item in items)
            {
                map.AddRange(item.ProductId, item.Tags);
            }

            return map;
        }

        public virtual async Task<Multimap<int, Discount>> GetAppliedDiscountsByProductIdsAsync(int[] productIds, bool includeHidden = false)
        {
            Guard.NotNull(productIds, nameof(productIds));

            var map = new Multimap<int, Discount>();
            if (!productIds.Any())
            {
                return map;
            }

            // NoTracking does not seem to eager load here.
            var query = _db.Products
                .Include(x => x.AppliedDiscounts.Select(y => y.RuleSets))
                .Where(x => productIds.Contains(x.Id))
                .ApplyStandardFilter(includeHidden)
                .Select(x => new
                {
                    ProductId = x.Id,
                    Discounts = x.AppliedDiscounts
                });

            var items = await query.ToListAsync();

            foreach (var item in items)
            {
                map.AddRange(item.ProductId, item.Discounts);
            }

            return map;
        }

        public virtual void ApplyProductReviewTotals(Product product)
        {
            Guard.NotNull(product, nameof(product));

            var approvedRatingSum = 0;
            var notApprovedRatingSum = 0;
            var approvedTotalReviews = 0;
            var notApprovedTotalReviews = 0;
            var reviews = product.ProductReviews;

            foreach (var pr in reviews)
            {
                if (pr.IsApproved)
                {
                    approvedRatingSum += pr.Rating;
                    approvedTotalReviews++;
                }
                else
                {
                    notApprovedRatingSum += pr.Rating;
                    notApprovedTotalReviews++;
                }
            }

            product.ApprovedRatingSum = approvedRatingSum;
            product.NotApprovedRatingSum = notApprovedRatingSum;
            product.ApprovedTotalReviews = approvedTotalReviews;
            product.NotApprovedTotalReviews = notApprovedTotalReviews;
        }

        // TODO: (mg) (core) Complete ProductService.AdjustInventoryAsync method.
        public virtual async Task<AdjustInventoryResult> AdjustInventoryAsync(Product product, bool decrease, int quantity, string attributesXml)
        {
            Guard.NotNull(product, nameof(product));

            var result = new AdjustInventoryResult();

            switch (product.ManageInventoryMethod)
            {
                case ManageInventoryMethod.ManageStock:
                    {
                        result.StockQuantityOld = product.StockQuantity;

                        result.StockQuantityNew = decrease
                            ? product.StockQuantity - quantity
                            : product.StockQuantity + quantity;

                        var newPublished = product.Published;
                        var newDisableBuyButton = product.DisableBuyButton;
                        var newDisableWishlistButton = product.DisableWishlistButton;

                        // Check if the minimum quantity is reached.
                        switch (product.LowStockActivity)
                        {
                            case LowStockActivity.DisableBuyButton:
                                newDisableBuyButton = product.MinStockQuantity >= result.StockQuantityNew;
                                newDisableWishlistButton = product.MinStockQuantity >= result.StockQuantityNew;
                                break;
                            case LowStockActivity.Unpublish:
                                newPublished = product.MinStockQuantity <= result.StockQuantityNew;
                                break;
                        }

                        product.StockQuantity = result.StockQuantityNew;
                        product.DisableBuyButton = newDisableBuyButton;
                        product.DisableWishlistButton = newDisableWishlistButton;
                        product.Published = newPublished;

                        // Send email notification.
                        if (decrease && product.NotifyAdminForQuantityBelow > result.StockQuantityNew)
                        {
                            //_services.MessageFactory.SendQuantityBelowStoreOwnerNotification(product, _localizationSettings.DefaultAdminLanguageId);
                        }
                    }
                    break;
                case ManageInventoryMethod.ManageStockByAttributes:
                    {
                        //var combination = _productAttributeParser.FindProductVariantAttributeCombination(product.Id, attributesXml);
                        ProductVariantAttributeCombination combination = null;
                        if (combination != null)
                        {
                            result.StockQuantityOld = combination.StockQuantity;

                            result.StockQuantityNew = decrease
                                ? combination.StockQuantity - quantity
                                : combination.StockQuantity + quantity;

                            combination.StockQuantity = result.StockQuantityNew;
                        }
                    }
                    break;
                case ManageInventoryMethod.DontManageStock:
                default:
                    // Do nothing.
                    break;
            }

            //var attributeValues = _productAttributeParser.ParseProductVariantAttributeValues(attributesXml);
            var attributeValues = new List<ProductVariantAttributeValue>();
            
            var productLinkageValues = attributeValues
                .Where(x => x.ValueType == ProductVariantAttributeValueType.ProductLinkage)
                .ToList();

            foreach (var chunk in productLinkageValues.Slice(100))
            {
                var linkedProductIds = chunk.Select(x => x.LinkedProductId).Distinct().ToArray();
                var linkedProducts = await _db.Products.GetManyAsync(linkedProductIds, true);
                var linkedProductsDic = linkedProducts.ToDictionarySafe(x => x.Id);

                foreach (var value in chunk)
                {
                    if (linkedProductsDic.TryGetValue(value.LinkedProductId, out var linkedProduct))
                    {
                        await AdjustInventoryAsync(linkedProduct, decrease, quantity * value.Quantity, string.Empty);
                    }
                }
            }

            return result;
        }

        public virtual async Task<int> EnsureMutuallyRelatedProductsAsync(int productId1)
        {
            var productQuery = _db.Products.ApplyStandardFilter(true);
            var relatedProductsQuery =
                from rp in _db.RelatedProducts
                join p in productQuery on rp.ProductId2 equals p.Id
                where rp.ProductId1 == productId1
                orderby rp.DisplayOrder
                select rp.ProductId2;

            var productIds = await relatedProductsQuery.ToListAsync();

            if (productIds.Count > 0 && !productIds.Any(x => x == productId1))
            {
                productIds.Add(productId1);
            }
            if (!productIds.Any())
            {
                return 0;
            }

            var query =
                from rp in _db.RelatedProducts
                join p in _db.Products on rp.ProductId2 equals p.Id
                where productIds.Contains(rp.ProductId2)
                select new { rp.ProductId1, rp.ProductId2 };

            var allAssociatedIds = await query.ToListAsync();
            var associatedIdsMap = allAssociatedIds.ToMultimap(x => x.ProductId2, x => x.ProductId1);
            var added = 0;

            foreach (var id1 in productIds)
            {
                var associatedIds = associatedIdsMap.ContainsKey(id1)
                    ? associatedIdsMap[id1]
                    : new List<int>();

                foreach (var id2 in productIds)
                {
                    if (id1 == id2)
                    {
                        continue;
                    }

                    if (!associatedIds.Any(x => x == id2))
                    {
                        var maxDisplayOrder = await _db.RelatedProducts
                            .Where(x => x.ProductId1 == id2)
                            .OrderByDescending(x => x.DisplayOrder)
                            .Select(x => x.DisplayOrder)
                            .FirstOrDefaultAsync();

                        await _db.RelatedProducts.AddAsync(new RelatedProduct
                        {
                            ProductId1 = id2,
                            ProductId2 = id1,
                            DisplayOrder = maxDisplayOrder + 1
                        });

                        ++added;
                    }
                }
            }

            return added;
        }

        public virtual async Task<int> EnsureMutuallyCrossSellProductsAsync(int productId1)
        {
            var productQuery = _db.Products.ApplyStandardFilter(true);
            var crossSellProductsQuery = 
                from csp in _db.CrossSellProducts
                join p in productQuery on csp.ProductId2 equals p.Id
                where csp.ProductId1 == productId1
                orderby csp.Id
                select csp.ProductId2;

            var productIds = await crossSellProductsQuery.ToListAsync();

            if (productIds.Count > 0 && !productIds.Any(x => x == productId1))
            {
                productIds.Add(productId1);
            }
            if (!productIds.Any())
            {
                return 0;
            }

            var query =
                from csp in _db.CrossSellProducts
                join p in _db.Products on csp.ProductId2 equals p.Id
                where productIds.Contains(csp.ProductId2)
                select new { csp.ProductId1, csp.ProductId2 };

            var allAssociatedIds = await query.ToListAsync();
            var associatedIdsMap = allAssociatedIds.ToMultimap(x => x.ProductId2, x => x.ProductId1);
            var added = 0;

            foreach (var id1 in productIds)
            {
                var associatedIds = associatedIdsMap.ContainsKey(id1)
                    ? associatedIdsMap[id1]
                    : new List<int>();

                foreach (var id2 in productIds)
                {
                    if (id1 == id2)
                    {
                        continue;
                    }

                    if (!associatedIds.Any(x => x == id2))
                    {
                        await _db.CrossSellProducts.AddAsync(new CrossSellProduct
                        {
                            ProductId1 = id2,
                            ProductId2 = id1
                        });

                        ++added;
                    }
                }
            }

            return added;
        }

        public virtual async Task<IList<Product>> GetCrossSellProductsByShoppingCartAsync(
            IList<OrganizedShoppingCartItem> cart,
            int numberOfProducts,
            bool includeHidden = false)
        {
            var result = new List<Product>();

            if (numberOfProducts == 0 || cart?.Count <= 0)
            {
                return result;
            }

            var cartProductIds = new HashSet<int>(cart.Select(x => x.Item.ProductId));

            var query = 
                from csp in _db.CrossSellProducts
                join p in _db.Products on csp.ProductId2 equals p.Id
                where cartProductIds.Contains(csp.ProductId1) && (includeHidden || p.Published)
                orderby csp.Id
                select csp;

            var csItems = await query.ToListAsync();
            var productIds1 = new HashSet<int>(csItems
                .Select(x => x.ProductId2)
                .Except(cartProductIds));

            if (productIds1.Any())
            {
                var productIds2 = productIds1.Take(numberOfProducts).ToArray();

                var products = await _db.Products
                    .Where(x => productIds2.Contains(x.Id))
                    .ToListAsync();

                result.AddRange(products.OrderBySequence(productIds2));
            }

            return result;
        }

        // TODO: (mg) (core) Add fluent validations when inserting ProductBundleItem.
        // if (bundleItem.BundleProductId == 0) throw new SmartException("BundleProductId of a bundle item cannot be 0.");
        // if (bundleItem.ProductId == 0) throw new SmartException("ProductId of a bundle item cannot be 0.");
        // if (bundleItem.ProductId == bundleItem.BundleProductId) throw new SmartException("A bundle item cannot be an element of itself.");

        public virtual async Task<IList<ProductBundleItemData>> GetBundleItemsAsync(
            int bundleProductId,
            bool includeHidden = false,
            bool tracked = false)
        {
            if (bundleProductId == 0)
            {
                return new List<ProductBundleItemData>();
            }

            var query =
                from pbi in _db.ProductBundleItem.ApplyTracking(tracked)
                join p in _db.Products.ApplyTracking(tracked) on pbi.ProductId equals p.Id
                where pbi.BundleProductId == bundleProductId && (includeHidden || (pbi.Published && p.Published))
                orderby pbi.DisplayOrder
                select pbi;

            query = query.Include(x => x.Product);

            var items = await query.ToListAsync();

            var bundleItemData = items
                .Select(x => new ProductBundleItemData(x))
                .ToList();

            return bundleItemData;
        }
    }
}
