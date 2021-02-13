﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Smartstore.Collections;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Net;
using Smartstore.Core.Web;
using Microsoft.AspNetCore.Authentication;

namespace Smartstore.Core.Content.Seo.Routing
{
    public class SlugRouteTransformer : DynamicRouteValueTransformer
    {
        private readonly SmartDbContext _db;
        private readonly IUrlService _urlService;
        private readonly IWebHelper _webHelper;
        private readonly LocalizationSettings _localizationSettings;
        private readonly ILanguageService _languageService;

        public SlugRouteTransformer(
            SmartDbContext db, 
            IUrlService urlService, 
            IWebHelper webHelper, 
            LocalizationSettings localizationSettings, 
            ILanguageService languageService)
        {
            _db = db;
            _urlService = urlService;
            _webHelper = webHelper;
            _localizationSettings = localizationSettings;
            _languageService = languageService;
        }

        #region Static

        public const string SlugRouteKey = "slug";

        // Key = Prefix, Value = EntityType
        private static readonly Multimap<string, string> _urlPrefixes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SlugRouter> _routers = new();

        static SlugRouteTransformer()
        {
            _routers.Add(new DefaultSlugRouter());
        }

        /// <summary>
        /// Gets all registered slug routers as ordered readonly sequence.
        /// </summary>
        public static IEnumerable<SlugRouter> Routers { get; } = _routers.OrderBy(x => x.Order);

        /// <summary>
        /// Registers a router that can generate route values for a matched <see cref="UrlRecord"/> entity.
        /// </summary>
        /// <param name="router">The router to register.</param>
        public static void RegisterRouter(SlugRouter router)
        {
            Guard.NotNull(router, nameof(router));
            _routers.Add(router);
        }

        /// <summary>
        /// Registers a url prefix for a range of entity names. 
        /// E.g. the prefix "shop" for entity name "product" would result in
        /// product url "shop/any-product-slug".
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="entityNames"></param>
        public static void RegisterUrlPrefix(string prefix, params string[] entityNames)
        {
            Guard.NotEmpty(prefix, nameof(prefix));
            _urlPrefixes.AddRange(prefix, entityNames);
        }

        public static string GetUrlPrefixFor(string entityName)
        {
            Guard.NotEmpty(entityName, nameof(entityName));

            if (_urlPrefixes.Count == 0)
                return null;

            return _urlPrefixes.FirstOrDefault(x => x.Value.Contains(entityName, StringComparer.OrdinalIgnoreCase)).Key;
        }

        private static bool TryResolveUrlPrefix(string slug, out string urlPrefix, out string actualSlug, out ICollection<string> entityNames)
        {
            urlPrefix = null;
            actualSlug = null;
            entityNames = null;

            if (_urlPrefixes.Count > 0)
            {
                var firstSepIndex = slug.IndexOf('/');
                if (firstSepIndex > 0)
                {
                    var prefix = slug.Substring(0, firstSepIndex);
                    if (_urlPrefixes.ContainsKey(prefix))
                    {
                        urlPrefix = prefix;
                        entityNames = _urlPrefixes[prefix];
                        actualSlug = slug[(prefix.Length + 1)..];
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        public override async ValueTask<RouteValueDictionary> TransformAsync(HttpContext httpContext, RouteValueDictionary values)
        {
            if (_webHelper.IsStaticResourceRequested())
            {
                return null;
            }

            var policy = _urlService.GetUrlPolicy();
            var slug = policy.Path.ToString();

            if (slug.IsEmpty())
            {
                return null;
            }

            if (TryResolveUrlPrefix(slug, out var urlPrefix, out var actualSlug, out var entityNames))
            {
                slug = actualSlug;
            }

            var urlRecord = await _db.UrlRecords
                .AsNoTracking()
                .ApplySlugFilter(slug, true)
                .FirstOrDefaultAsync();

            if (urlRecord == null)
            {
                return null;
            }

            // INFO: The inline GetSlugCulture() method needs this
            string _slugCulture = null;

            if (!urlRecord.IsActive)
            {
                // Found slug is outdated. Find the latest active one.
                var activeSlug = await _urlService.GetActiveSlugAsync(urlRecord.EntityId, urlRecord.EntityName, urlRecord.LanguageId);
                if (activeSlug.HasValue())
                {
                    // Apply a permanent response redirect to active slug
                    policy.Culture.Modify(await GetSlugCulture());
                    policy.Path.Modify(UrlPolicy.CombineSegments(urlPrefix, activeSlug));
                }

                return null;
            }

            if (_localizationSettings.SeoFriendlyUrlsForLanguagesEnabled)
            {
                var defaultCulture = policy.DefaultCultureCode;
                var requestCulture = (string)policy.Culture;
                var ambientCulture = requestCulture ?? defaultCulture;
                var slugCulture = await GetSlugCulture();

                if (requestCulture == null && _localizationSettings.DefaultLanguageRedirectBehaviour == DefaultLanguageRedirectBehaviour.PrependSeoCodeAndRedirect)
                {
                    // table > en/table
                    policy.Culture.Modify(defaultCulture);
                    policy.Path.Modify(UrlPolicy.CombineSegments(urlPrefix, slug));
                    return null;
                }
                else if (ambientCulture != slugCulture && _languageService.IsPublishedLanguage(ambientCulture))
                {
                    // Current slug language differs from requested languages. This can happen if a language switch was performed.
                    // We have to determine the request language...
                    var ambientLanguage = await _db.Languages.FirstOrDefaultAsync(x => x.UniqueSeoCode == ambientCulture);
                    // ...then determine the active slug for the request language.
                    var ambientSlug = await _urlService.GetActiveSlugAsync(urlRecord.EntityId, urlRecord.EntityName, ambientLanguage.Id);
                    
                    if (ambientSlug.HasValue() && ambientSlug != slug)
                    {
                        // ...now check if request language is default
                        if (ambientCulture.EqualsNoCase(defaultCulture) && _localizationSettings.DefaultLanguageRedirectBehaviour == DefaultLanguageRedirectBehaviour.StripSeoCode)
                        {
                            // ...and culture code should be stripped off URLs when default language
                            policy.Culture.Strip();
                        }
                        else
                        {
                            // Now request the direction to the new location:
                            // tisch > en/table
                            // en/table > tisch
                            // en/table > tr/masa
                            // etc.
                            policy.Culture.Modify(ambientCulture);
                        }

                        policy.Path.Modify(UrlPolicy.CombineSegments(urlPrefix, ambientSlug));
                        return null;
                    }
                }
            }

            // Verify prefix matches any assigned entity name
            if (entityNames != null && !entityNames.Contains(urlRecord.EntityName, StringComparer.OrdinalIgnoreCase))
            {
                // does NOT match
                return null;
            }

            var transformedValues = Routers.Select(x => x.GetRouteValues(urlRecord, values)).FirstOrDefault();
            if (transformedValues == null)
            {
                return null;
            }

            transformedValues[SlugRouteKey] = slug;
            httpContext.GetRouteData().DataTokens["UrlRecord"] = urlRecord;

            return transformedValues;

            async Task<string> GetSlugCulture()
            {
                return (_slugCulture ??= (await _db.Languages.FindByIdAsync(urlRecord.LanguageId))?.GetTwoLetterISOLanguageName().EmptyNull()).NullEmpty();
            }
        }
    }
}