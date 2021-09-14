﻿using System;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core;
using Smartstore.Core.Widgets;
using Smartstore.Web.Controllers;

namespace Smartstore.Blog.Filter
{
    public class TopBarMenuFilter : IResultFilter
    {
        private readonly BlogSettings _blogSettings;
        private readonly IWidgetProvider _widgetProvider;
        private readonly ICommonServices _services;
        private readonly Lazy<IUrlHelper> _urlHelper;

        public TopBarMenuFilter(BlogSettings blogSettings, IWidgetProvider widgetProvider, ICommonServices services, Lazy<IUrlHelper> urlHelper)
        {
            _blogSettings = blogSettings;
            _widgetProvider = widgetProvider;
            _services = services;
            _urlHelper = urlHelper;
        }

        public void OnResultExecuting(ResultExecutingContext filterContext)
        {
            if (!_blogSettings.Enabled)
                return;

            if (filterContext.Controller is not PublicController)
                return;

            var result = filterContext.Result;

            // should only run on a full view rendering result or HTML ContentResult
            if (!result.IsHtmlViewResult())
                return;

            var html = $"<a class='menubar-link' href='{_urlHelper.Value.RouteUrl("Blog")}'>{_services.Localization.GetResource("Blog")}</a>";

            _widgetProvider.RegisterHtml(new[] { "header_menu_special"}, new HtmlString(html));
        }

        public void OnResultExecuted(ResultExecutedContext filterContext)
        {
        }
    }
}
