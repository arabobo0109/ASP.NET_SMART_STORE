﻿using System.Globalization;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.GiftCards;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Web;
using Smartstore.Utilities;
using Smartstore.Utilities.Html;

namespace Smartstore.Core.Catalog.Attributes
{
    public partial class ProductAttributeFormatter : IProductAttributeFormatter
    {
        private readonly SmartDbContext _db;
        private readonly IWorkContext _workContext;
        private readonly IWebHelper _webHelper;
        private readonly IProductAttributeMaterializer _productAttributeMaterializer;
        private readonly ILocalizationService _localizationService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly PriceSettings _priceSettings;

        public ProductAttributeFormatter(
            SmartDbContext db,
            IWorkContext workContext,
            IWebHelper webHelper,
            IProductAttributeMaterializer productAttributeMaterializer,
            ILocalizationService localizationService,
            IPriceCalculationService priceCalculationService,
            ShoppingCartSettings shoppingCartSettings,
            PriceSettings priceSettings)
        {
            _db = db;
            _workContext = workContext;
            _webHelper = webHelper;
            _productAttributeMaterializer = productAttributeMaterializer;
            _localizationService = localizationService;
            _priceCalculationService = priceCalculationService;
            _shoppingCartSettings = shoppingCartSettings;
            _priceSettings = priceSettings;
        }

        public virtual async Task<string> FormatAttributesAsync(
            ProductVariantAttributeSelection selection,
            Product product,
            Customer customer = null,
            string separator = "<br />",
            bool htmlEncode = true,
            bool includePrices = true,
            bool includeProductAttributes = true,
            bool includeGiftCardAttributes = true,
            bool includeHyperlinks = true,
            ProductBatchContext batchContext = null)
        {
            Guard.NotNull(selection);
            Guard.NotNull(product);

            customer ??= _workContext.CurrentCustomer;

            using var pool = StringBuilderPool.Instance.Get(out var result);

            if (includeProductAttributes)
            {
                var language = _workContext.WorkingLanguage;
                var attributes = await _productAttributeMaterializer.MaterializeProductVariantAttributesAsync(selection);

                // Key: ProductVariantAttributeValue.Id, value: calculated attribute price adjustment.
                var priceAdjustments = includePrices && _priceSettings.ShowVariantCombinationPriceAdjustment
                    ? await _priceCalculationService.CalculateAttributePriceAdjustmentsAsync(product, selection, 1, _priceCalculationService.CreateDefaultOptions(false, customer, null, batchContext))
                    : new Dictionary<int, CalculatedPriceAdjustment>();

                foreach (var pva in attributes)
                {
                    var pair = selection.AttributesMap.FirstOrDefault(x => x.Key == pva.Id);
                    if (pair.Key == 0)
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        var valueStr = value.ToString().EmptyNull();
                        var pvaAttribute = string.Empty;

                        if (pva.IsListTypeAttribute())
                        {
                            var pvaValue = pva.ProductVariantAttributeValues.FirstOrDefault(x => x.Id == valueStr.ToInt());
                            if (pvaValue != null)
                            {
                                pvaAttribute = $"{pva.ProductAttribute.GetLocalized(x => x.Name, language.Id)}: {pvaValue.GetLocalized(x => x.Name, language.Id)}";

                                if (includePrices)
                                {
                                    if (_shoppingCartSettings.ShowLinkedAttributeValueQuantity &&
                                        pvaValue.ValueType == ProductVariantAttributeValueType.ProductLinkage &&
                                        pvaValue.Quantity > 1)
                                    {
                                        pvaAttribute = pvaAttribute + " × " + pvaValue.Quantity;
                                    }

                                    if (priceAdjustments.TryGetValue(pvaValue.Id, out var adjustment))
                                    {
                                        if (adjustment.Price > 0)
                                        {
                                            pvaAttribute += $" (+{adjustment.Price})";
                                        }
                                        else if (adjustment.Price < 0)
                                        {
                                            pvaAttribute += $" (-{adjustment.Price * -1})";
                                        }
                                    }
                                }

                                if (htmlEncode)
                                {
                                    pvaAttribute = pvaAttribute.HtmlEncode();
                                }
                            }
                        }
                        else if (pva.AttributeControlType == AttributeControlType.MultilineTextbox)
                        {
                            string attributeName = pva.ProductAttribute.GetLocalized(x => x.Name, language.Id);
                            pvaAttribute = $"{(htmlEncode ? attributeName.HtmlEncode() : attributeName)}: {HtmlUtility.ConvertPlainTextToHtml(valueStr.HtmlEncode())}";
                        }
                        else if (pva.AttributeControlType == AttributeControlType.FileUpload)
                        {
                            if (Guid.TryParse(valueStr, out var downloadGuid) && downloadGuid != Guid.Empty)
                            {
                                var download = await _db.Downloads
                                    .AsNoTracking()
                                    .Include(x => x.MediaFile)
                                    .Where(x => x.DownloadGuid == downloadGuid)
                                    .FirstOrDefaultAsync();

                                if (download?.MediaFile != null)
                                {
                                    var attributeText = string.Empty;
                                    var fileName = htmlEncode
                                        ? download.MediaFile.Name.HtmlEncode()
                                        : download.MediaFile.Name;

                                    if (includeHyperlinks)
                                    {
                                        // TODO: (core) add a method for getting URL (use routing because it handles all SEO friendly URLs).
                                        var downloadLink = _webHelper.GetStoreLocation() + "download/getfileupload/?downloadId=" + download.DownloadGuid;
                                        attributeText = $"<a href='{downloadLink}' class='fileuploadattribute'>{fileName}</a>";
                                    }
                                    else
                                    {
                                        attributeText = fileName;
                                    }

                                    string attributeName = pva.ProductAttribute.GetLocalized(x => x.Name, language.Id);
                                    pvaAttribute = $"{(htmlEncode ? attributeName.HtmlEncode() : attributeName)}: {attributeText}";
                                }
                            }
                        }
                        else
                        {
                            // TextBox, Datepicker
                            if (pva.AttributeControlType == AttributeControlType.Datepicker)
                            {
                                CultureInfo culture = null;
                                try
                                {
                                    culture = new CultureInfo(language.LanguageCulture);
                                }
                                catch
                                {
                                }

                                valueStr = valueStr.ToDateTime(null)?.ToString("D", culture) ?? valueStr;
                            }

                            pvaAttribute = $"{pva.ProductAttribute.GetLocalized(x => x.Name, language.Id)}: {valueStr}";

                            if (htmlEncode)
                            {
                                pvaAttribute = pvaAttribute.HtmlEncode();
                            }
                        }

                        result.Grow(pvaAttribute, separator);
                    }
                }
            }

            if (includeGiftCardAttributes && product.IsGiftCard)
            {
                var gci = selection.GetGiftCardInfo();
                if (gci != null)
                {
                    // Sender.
                    var giftCardFrom = product.GiftCardType == GiftCardType.Virtual
                        ? _localizationService.GetResource("GiftCardAttribute.From.Virtual").FormatInvariant(gci.SenderName, gci.SenderEmail)
                        : _localizationService.GetResource("GiftCardAttribute.From.Physical").FormatInvariant(gci.SenderName);

                    // Recipient.
                    var giftCardFor = product.GiftCardType == GiftCardType.Virtual
                        ? _localizationService.GetResource("GiftCardAttribute.For.Virtual").FormatInvariant(gci.RecipientName, gci.RecipientEmail)
                        : _localizationService.GetResource("GiftCardAttribute.For.Physical").FormatInvariant(gci.RecipientName);

                    if (htmlEncode)
                    {
                        giftCardFrom = giftCardFrom.HtmlEncode();
                        giftCardFor = giftCardFor.HtmlEncode();
                    }

                    result.Grow(giftCardFrom, separator);
                    result.Grow(giftCardFor, separator);
                }
            }

            return result.ToString();
        }
    }
}
