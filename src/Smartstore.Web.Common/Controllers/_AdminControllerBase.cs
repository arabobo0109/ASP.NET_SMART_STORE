﻿using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Core;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Security;
using Smartstore.Web.Theming;

namespace Smartstore.Web.Controllers
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class NonAdminAttribute : ActionFilterAttribute
    {
        public NonAdminAttribute()
        {
            // Must come after AdminThemedAttribute.
            Order = 100;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            context.HttpContext.RequestServices.GetRequiredService<IWorkContext>().IsAdminArea = false;
        }
    }

    [Area("Admin")]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    //[AdminValidateIpAddress]
    [RequireSsl]
    [AdminThemed]
    [TrackActivity(Order = 100)]
    [SaveChanges(typeof(SmartDbContext), Order = int.MaxValue)]
    public abstract class AdminControllerBase : ManageController
    {
    }
}
