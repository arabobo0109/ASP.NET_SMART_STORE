﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Customers;
using Smartstore.Core.Localization;

namespace Smartstore
{
    /// <summary>
    /// Shopping cart extension methods
    /// </summary>
    public static class ShoppingCartExtensions
    {
        /// <summary>
        /// Finds and returns first matching product from shopping cart.
        /// </summary>
        /// <remarks>
        /// Products with the same identifier need to have matching attribute selections as well.
        /// </remarks>
        /// <param name="cart">Shopping cart to search in.</param>
        /// <param name="shoppingCartType">Shopping cart type to search in.</param>
        /// <param name="product">Product to search for.</param>
        /// <param name="selection">Attribute selection.</param>
        /// <param name="customerEnteredPrice">Customers entered price needs to match (if enabled by product).</param>
        /// <returns>Matching <see cref="OrganizedShoppingCartItem"/> or <c>null</c> if none was found.</returns>
        public static OrganizedShoppingCartItem FindItemInCart(
            this IList<OrganizedShoppingCartItem> cart,
            ShoppingCartType shoppingCartType,
            Product product,
            ProductVariantAttributeSelection selection,
            decimal customerEnteredPrice = decimal.Zero)
        {
            Guard.NotNull(cart, nameof(cart));
            Guard.NotNull(product, nameof(product));

            // Return on product bundle with individual item pricing - too complex
            if (product.ProductType == ProductType.BundledProduct && product.BundlePerItemPricing)
                return null;

            // Filter non group items from correct cart type, with matching product id and product type id
            var filteredCart = cart
                .Where(x => x.Item.ShoppingCartType == shoppingCartType
                && x.Item.ParentItemId == null
                && x.Item.Product.ProductTypeId == product.ProductTypeId
                && x.Item.ProductId == product.Id);

            // There could be multiple matching products with the same identifier but different attributes/selections (etc).
            // Ensure matching product infos are the same (attributes, gift card values (if it is one), customerEnteredPrice).
            foreach (var cartItem in filteredCart)
            {
                // Compare attribute selection
                var cartItemSelection = cartItem.Item.AttributeSelection;
                if (cartItemSelection != selection)
                    continue;

                var currentProduct = cartItem.Item.Product;

                // Compare gift cards info values (if it is a gift card)
                if (currentProduct.IsGiftCard &&
                    (cartItemSelection.GiftCardInfo == null
                    || selection.GiftCardInfo == null
                    || cartItemSelection != selection))
                {
                    continue;
                }

                // Products with CustomerEntersPrice are equal if the price is the same.
                // But a system product may only be placed once in the shopping cart.
                if (currentProduct.CustomerEntersPrice && !currentProduct.IsSystemProduct
                    && Math.Round(cartItem.Item.CustomerEnteredPrice, 2) != Math.Round(customerEnteredPrice, 2))
                {
                    continue;
                }

                // If we got this far, we found a matching product with the same values
                return cartItem;
            }

            return null;
        }


        /// <summary>
        /// Checks whether the shopping cart requires shipping.
        /// </summary>
        /// <returns>
        /// <c>True</c> if any product requires shipping; otherwise <c>false</c>.
        /// </returns>
        public static bool IsShippingRequired(this IEnumerable<OrganizedShoppingCartItem> cart)
        {
            Guard.NotNull(cart, nameof(cart));

            return cart.Where(x => x.Item.IsShippingEnabled).Any();
        }

        /// <summary>
        /// Gets the total quantity of products in the cart.
        /// </summary>
		public static int GetTotalQuantity(this IEnumerable<OrganizedShoppingCartItem> cart)
        {
            Guard.NotNull(cart, nameof(cart));

            return cart.Sum(x => x.Item.Quantity);
        }

        /// <summary>
        /// Gets a value indicating whether the cart includes products matching the condition.
        /// </summary>
        /// <param name="matcher">The condition to match cart items.</param>
        /// <returns>
        /// <c>True</c> if any product matches the condition; otherwise <c>false</c>.
        /// <see cref=""/>
        /// </returns>
        public static bool IncludesMatchingItems(this IEnumerable<OrganizedShoppingCartItem> cart, Func<Product, bool> matcher)
        {
            Guard.NotNull(cart, nameof(cart));

            return cart.Where(x => x.Item.Product != null && matcher(x.Item.Product)).Any();
        }

        /// <summary>
        /// Gets the recurring cycle information.
        /// <param name="localizationService">The localization service.</param>
        /// </summary>
        /// <returns>
        /// <see cref="RecurringCycleInfo"/>
        /// </returns>
		public static RecurringCycleInfo GetRecurringCycleInfo(this IEnumerable<OrganizedShoppingCartItem> cart, ILocalizationService localizationService)
        {
            Guard.NotNull(cart, nameof(cart));
            Guard.NotNull(localizationService, nameof(localizationService));

            var cycleInfo = new RecurringCycleInfo();

            foreach (var organizedItem in cart)
            {
                var product = organizedItem.Item.Product;
                if (product is null)
                    throw new SmartException(string.Format("Product (Id={0}) cannot be loaded", organizedItem.Item.ProductId));

                if (!product.IsRecurring)
                    continue;

                if (!cycleInfo.HasValues)
                {
                    cycleInfo.CycleLength = product.RecurringCycleLength;
                    cycleInfo.CyclePeriod = product.RecurringCyclePeriod;
                    cycleInfo.TotalCycles = product.RecurringTotalCycles;
                    continue;
                }

                if (cycleInfo.CycleLength != product.RecurringCycleLength
                    || cycleInfo.CyclePeriod != product.RecurringCyclePeriod
                    || cycleInfo.CyclePeriod != product.RecurringCyclePeriod)
                {
                    cycleInfo.ErrorMessage = localizationService.GetResource("ShoppingCart.ConflictingShipmentSchedules");
                    break;
                }
            }

            return cycleInfo;
        }

        /// <summary>
        /// Gets customer of shopping cart.
        /// </summary>
        /// <returns>
        /// <see cref="Customer"/> of <see cref="OrganizedShoppingCartItem"/> or <c>null</c> if cart is empty.
        /// </returns>
        public static Customer GetCustomer(this IList<OrganizedShoppingCartItem> cart)
        {
            Guard.NotNull(cart, nameof(cart));

            return cart.Count > 0 ? cart[0].Item.Customer : null;
        }

        /// <summary>
        /// Removes a single cart item from shopping cart of <see cref="ShoppingCartItem.Customer"/>.
        /// </summary>        
        /// <param name="cartItem">Cart item to remove from shopping cart.</param>
        /// <param name="resetCheckoutData">A value indicating whether to reset checkout data.</param>
        /// <param name="removeInvalidCheckoutAttributes">A value indicating whether to remove incalid checkout attributes.</param>
        /// <param name="deleteChildCartItems">A value indicating whether to delete child cart items of <c>cartItem.</c></param>
        /// <returns>Number of deleted entries.</returns>
        public static Task<int> DeleteCartItemAsync(
            this IShoppingCartService cartService,
            ShoppingCartItem cartItem,
            bool resetCheckoutData = true,
            bool removeInvalidCheckoutAttributes = false,
            bool deleteChildCartItems = true)
        {
            Guard.NotNull(cartService, nameof(cartService));
            Guard.NotNull(cartItem, nameof(cartItem));

            return cartService.DeleteCartItemsAsync(
                new List<ShoppingCartItem>() { cartItem },
                resetCheckoutData,
                removeInvalidCheckoutAttributes,
                deleteChildCartItems);
        }

        /// <summary>
        /// Validates a single cart item for bundle items.
        /// </summary>
        /// <param name="cartValidator"></param>
        /// <param name="bundleItem"></param>
        /// <returns></returns>
        public static IList<string> ValidateBundleItem(this IShoppingCartValidator cartValidator, ProductBundleItem bundleItem)
        {
            Guard.NotNull(cartValidator, nameof(cartValidator));
            Guard.NotNull(bundleItem, nameof(bundleItem));

            return cartValidator.ValidateBundleItems(new List<ProductBundleItem> { bundleItem });
        }
    }
}
