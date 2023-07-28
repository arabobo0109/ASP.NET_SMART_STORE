﻿using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.DataExchange.Import;
using Smartstore.Core.Identity;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations
{
    public class SmartDbContextDataSeeder : IDataSeeder<SmartDbContext>
    {
        public bool RollbackOnFailure => false;

        public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
        {
            await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
            await MigrateSettingsAsync(context, cancelToken);
        }

        public async Task MigrateSettingsAsync(SmartDbContext db, CancellationToken cancelToken = default)
        {
            var enableCookieConsentSettings = await db.Settings
                .Where(x => x.Name == "PrivacySettings.EnableCookieConsent")
                .ToListAsync(cancelToken);

            if (enableCookieConsentSettings.Count > 0)
            {
                foreach (var setting in enableCookieConsentSettings)
                {
                    db.Settings.Add(new Setting
                    {
                        Name = "PrivacySettings.CookieConsentRequirement",
                        Value = setting.Value.ToBool() ? CookieConsentRequirement.RequiredInEUCountriesOnly.ToString() : CookieConsentRequirement.NeverRequired.ToString(),
                        StoreId = setting.StoreId
                    });
                }
            }

            await db.SaveChangesAsync(cancelToken);

            await MigrateOfflinePaymentDescriptions(db, cancelToken);
            await MigrateImportProfileColumnMappings(db, cancelToken);
        }

        public void MigrateLocaleResources(LocaleResourcesBuilder builder)
        {
            builder.AddOrUpdate("Admin.Configuration.Settings.Order.GiftCards_Activated")
                .Value("de", "Geschenkgutschein ist aktiviert, wenn Auftragsstatus...");

            builder.AddOrUpdate("Admin.Configuration.Settings.Order.GiftCards_Activated.Hint")
                .Value("de", "Legt den Auftragsstatus einer Bestellung fest, bei dem in der Bestellung enthaltene Geschenkgutscheine automatisch aktiviert werden.");

            builder.Delete(
                "Admin.Configuration.Currencies.GetLiveRates",
                "Common.Error.PreProcessPayment",
                "Payment.PayingFailed");

            builder.AddOrUpdate("Enums.DataExchangeCompletionEmail.Always", "Always", "Immer");
            builder.AddOrUpdate("Enums.DataExchangeCompletionEmail.OnError", "If an error occurs", "Bei einem Fehler");
            builder.AddOrUpdate("Enums.DataExchangeCompletionEmail.Never", "Never", "Nie");

            builder.AddOrUpdate("Admin.Configuration.Settings.DataExchange.ImportCompletionEmail",
                "Import completion email",
                "E-Mail zum Importabschluss",
                "Specifies whether an email should be sent when an import has completed.",
                "Legt fest, ob eine E-Mail bei Abschluss eines Imports verschickt werden soll.");

            builder.Update("Admin.Configuration.Payment.Methods.Fields.RecurringPaymentType").Value("de", "Abo Zahlungen");
            builder.Update("Admin.Plugins.LicensingDemoRemainingDays").Value("de", "Demo: noch {0} Tag(e)");
        }

        /// <summary>
        /// Migrates obsolete payment description setting (xyzSettings.DescriptionText) of offline payment methods.
        /// </summary>
        private static async Task MigrateOfflinePaymentDescriptions(SmartDbContext db, CancellationToken cancelToken)
        {
            var names = new Dictionary<string, string>
            {
                { "Payments.CashOnDelivery", "CashOnDeliveryPaymentSettings.DescriptionText" },
                { "Payments.DirectDebit", "DirectDebitPaymentSettings.DescriptionText" },
                { "Payments.Invoice", "InvoicePaymentSettings.DescriptionText" },
                { "Payments.Manual", "ManualPaymentSettings.DescriptionText" },
                { "Payments.PurchaseOrderNumber", "PurchaseOrderNumberPaymentSettings.DescriptionText" },
                { "Payments.PayInStore", "PayInStorePaymentSettings.DescriptionText" },
                { "Payments.Prepayment", "PrepaymentPaymentSettings.DescriptionText" }
            };

            var settingNames = names.Values.ToList();
            var descriptionSettings = (await db.Settings
                .AsNoTracking()
                .Where(x => settingNames.Contains(x.Name) && x.StoreId == 0)
                .ToListAsync(cancelToken))
                .ToDictionarySafe(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
            if (descriptionSettings.Count == 0)
            {
                return;
            }

            var masterLanguageId = await db.Languages
                .Where(x => x.Published)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancelToken);
            if (masterLanguageId == 0)
            {
                return;
            }

            var systemNames = names.Keys.ToArray();
            var paymentMethods = (await db.PaymentMethods
                .Where(x => systemNames.Contains(x.PaymentMethodSystemName))
                .ToListAsync(cancelToken))
                .ToDictionarySafe(x => x.PaymentMethodSystemName, x => x, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in names)
            {
                var systemName = pair.Key;
                var settingName = pair.Value;

                if (paymentMethods.TryGetValue(systemName, out var pm) && pm != null && pm.FullDescription.HasValue())
                {
                    // Nothing to do. Do not overwrite.
                    continue;
                }

                if (descriptionSettings.TryGetValue(settingName, out var setting) && setting.Value.HasValue())
                {
                    var description = setting.Value;
                    if (description.StartsWithNoCase("@Plugins.Payment"))
                    {
                        var resourceName = description[1..];
                        
                        description = await db.LocaleStringResources
                            .Where(x => x.LanguageId == masterLanguageId && x.ResourceName == resourceName)
                            .Select(x => x.ResourceValue)
                            .FirstOrDefaultAsync(cancelToken);
                    }

                    if (description.HasValue())
                    {
                        // Ignore PaymentMethod's localized properties. The old xyzSettings.DescriptionText had no localization at all.
                        if (pm == null)
                        {
                            db.PaymentMethods.Add(new PaymentMethod
                            {
                                PaymentMethodSystemName = systemName,
                                FullDescription = description,
                            });
                        }
                        else
                        {
                            pm.FullDescription = description;
                        }
                    }
                }
            }

            await db.SaveChangesAsync(cancelToken);

            // Delete obsolete offline payment settings.
            settingNames.AddRange(new[]
            {
                "CashOnDeliveryPaymentSettings.ThumbnailPictureId",
                "DirectDebitPaymentSettings.ThumbnailPictureId",
                "InvoicePaymentSettings.ThumbnailPictureId",
                "ManualPaymentSettings.ThumbnailPictureId",
                "PurchaseOrderNumberPaymentSettings.ThumbnailPictureId",
                "PayInStorePaymentSettings.ThumbnailPictureId",
                "PrepaymentPaymentSettings.ThumbnailPictureId"
            });

            await db.Settings
                .Where(x => settingNames.Contains(x.Name))
                .ExecuteDeleteAsync(cancelToken);
        }

        /// <summary>
        /// Migrates the import profile column mapping of localized properties (if any) from language SEO code to language culture for most languages (see issue #531).
        /// </summary>
        private static async Task MigrateImportProfileColumnMappings(SmartDbContext db, CancellationToken cancelToken)
        {
            try
            {
                var profiles = await db.ImportProfiles
                    .Where(x => x.ColumnMapping.Length > 3)
                    .ToListAsync(cancelToken);
                if (profiles.Count == 0)
                {
                    return;
                }

                var mapConverter = new ColumnMapConverter();
                var allLanguages = await db.Languages.AsNoTracking().OrderBy(x => x.DisplayOrder).ToListAsync(cancelToken);
                var fallbackCultures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "aa", "ar-SA" },
                    { "cs", "cs-CZ" },
                    { "da", "da-DK" },
                    { "el", "el-GR" },
                    { "en", "en-US" },
                    { "et", "et-EE" },
                    { "he", "he-IL" },
                    { "hi", "hi-IN" },
                    { "ja", "ja-JP" },
                    { "ko", "ko-KR" },
                    { "sl", "sl-SI" },
                    { "sq", "sq-AL" },
                    { "sv", "sv-SE" },
                    { "uk", "uk-UA" },
                    { "vi", "vi-VN" },
                    { "zh", "zh-CN" },
                };

                foreach (var profile in profiles)
                {
                    var storedMap = mapConverter.ConvertFrom<ColumnMap>(profile.ColumnMapping);
                    if (storedMap == null)
                        continue;

                    var map = new ColumnMap();
                    var update = false;

                    foreach (var mapping in storedMap.Mappings.Select(x => x.Value))
                    {
                        var mappedName = mapping.MappedName;

                        if (MapColumnName(mappedName, out string name2, out string index2))
                        {
                            mappedName = $"{name2}[{index2}]";
                            update = true;
                        }

                        if (MapColumnName(mapping.SourceName, out string name, out string index))
                        {
                            update = true;
                        }

                        map.AddMapping(name, index, mappedName, mapping.Default);
                    }

                    if (update)
                    {
                        profile.ColumnMapping = mapConverter.ConvertTo(map);
                    }
                }

                await db.SaveChangesAsync(cancelToken);

                bool MapColumnName(string sourceName, out string name, out string index)
                {
                    ColumnMap.ParseSourceName(sourceName, out name, out index);

                    if (name.HasValue() && index.HasValue() && index.Length == 2)
                    {
                        var seoCode = index;
                        var newCulture = $"{seoCode.ToLowerInvariant()}-{seoCode.ToUpperInvariant()}";

                        var language = 
                            allLanguages.FirstOrDefault(x => x.LanguageCulture.EqualsNoCase(newCulture)) ??
                            allLanguages.FirstOrDefault(x => x.UniqueSeoCode.EqualsNoCase(seoCode));

                        if (language != null)
                        {
                            index = language.LanguageCulture;
                            return true;
                        }

                        if (fallbackCultures.TryGetValue(index, out newCulture))
                        {
                            index = newCulture;
                            return true;
                        }
                    }

                    return false;
                }
            }
            catch
            {
            }
        }
    }
}