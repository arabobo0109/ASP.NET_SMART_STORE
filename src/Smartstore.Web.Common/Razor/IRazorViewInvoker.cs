﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Smartstore.Web.Razor
{
    public interface IRazorViewInvoker
    {
        /// <summary>
        /// Invokes a view and returns its html content.
        /// </summary>
        /// <param name="viewName">View name</param>
        /// <param name="model">Model</param>
        /// <param name="isMainPage"><c>false</c>: View is partial</param>
        /// <returns>View rendering result</returns>
        Task<IHtmlContent> InvokeViewAsync<TModel>(string viewName, TModel model, bool isMainPage = false);

        /// <summary>
        /// Invokes a view component and returns its html content.
        /// </summary>
        /// <param name="viewData">View name</param>
        /// <param name="componentName">The view component name.</param>
        /// <param name="arguments">
        /// An <see cref="object"/> with properties representing arguments to be passed to the invoked view component
        /// method. Alternatively, an <see cref="System.Collections.Generic.IDictionary{String, Object}"/> instance
        /// containing the invocation arguments.
        /// </param>
        /// <returns>View component rendering result</returns>
        Task<IHtmlContent> InvokeViewComponentAsync(ViewDataDictionary viewData, string componentName, object arguments);

        /// <summary>
        /// Invokes a view component and returns its html content.
        /// </summary>
        /// <param name="viewData">View name</param>
        /// <param name="componentType">The view component type.</param>
        /// <param name="arguments">
        /// An <see cref="object"/> with properties representing arguments to be passed to the invoked view component
        /// method. Alternatively, an <see cref="System.Collections.Generic.IDictionary{String, Object}"/> instance
        /// containing the invocation arguments.
        /// </param>
        /// <returns>View component rendering result</returns>
        Task<IHtmlContent> InvokeViewComponentAsync(ViewDataDictionary viewData, Type componentType, object arguments);
    }
}
