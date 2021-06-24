﻿using Microsoft.AspNetCore.Mvc;
using Smartstore.Caching;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Data.Caching;
using Smartstore.Web.Controllers;
using System.Threading.Tasks;

namespace Smartstore.Web.Areas.Admin.Controllers
{
    public class CommonController : AdminControllerBase
    {
        private readonly SmartDbContext _db;
        private readonly IRequestCache _requestCache;
        private readonly IDbCache _dbCache;

        // TODO: (mh) (core) dbCache cannot be resolved. Breaks...
        public CommonController(SmartDbContext db, IRequestCache requestCache/*, IDbCache dbCache*/)
        {
            _db = db;
            _requestCache = requestCache;
            //_dbCache = dbCache;
        }

        public async Task<IActionResult> LanguageSelected(int customerlanguage)
        {
            var language = await _db.Languages.FindByIdAsync(customerlanguage, false);
            if (language != null && language.Published)
            {
                Services.WorkContext.WorkingLanguage = language;
            }

            return Content(T("Admin.Common.DataEditSuccess"));
        }

        [Permission(Permissions.System.Maintenance.Execute)]
        [HttpPost]
        public IActionResult RestartApplication()
        {
            // TODO: (mh) (core) This must be tested in production environment. In VS _hostApplicationLifetime.StopApplication() just stops without restarting on next request.
            Services.WebHelper.RestartAppDomain();

            return new JsonResult(null);
        }

        [Permission(Permissions.System.Maintenance.Execute)]
        [HttpPost]
        public IActionResult ClearCache()
        {
            Services.Cache.Clear();

            _requestCache.RemoveByPattern("*");

            return new JsonResult
            (
                new
                {
                    Success = true,
                    Message = T("Admin.Common.TaskSuccessfullyProcessed").Value
                }
            );
        }

        [Permission(Permissions.System.Maintenance.Execute)]
        [HttpPost]
        public ActionResult ClearDatabaseCache()
        {
            // TODO: (mh) (core) Uncomment when dbCache can be resolved.
            //_dbCache.Clear();

            return new JsonResult
            (
                new
                {
                    Success = true,
                    Message = T("Admin.Common.TaskSuccessfullyProcessed").Value
                }
            );
        }
    }
}
