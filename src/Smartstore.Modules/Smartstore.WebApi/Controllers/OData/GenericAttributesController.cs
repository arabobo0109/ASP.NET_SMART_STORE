﻿namespace Smartstore.Web.Api.Controllers.OData
{
    /// <summary>
    /// The endpoint for operations on GenericAttribute entity.
    /// </summary>
    public class GenericAttributesController : WebApiController<GenericAttribute>
    {
        [HttpGet, ApiQueryable]
        public IQueryable<GenericAttribute> Get()
        {
            return Entities.AsNoTracking();
        }

        [HttpGet, ApiQueryable]
        public SingleResult<GenericAttribute> Get(int key)
        {
            return GetById(key);
        }

        [HttpPost]
        public Task<IActionResult> Post([FromBody] GenericAttribute entity)
        {
            return PostAsync(entity);
        }

        [HttpPut]
        public Task<IActionResult> Put(int key, Delta<GenericAttribute> model)
        {
            return PutAsync(key, model);
        }

        [HttpPatch]
        public Task<IActionResult> Patch(int key, Delta<GenericAttribute> model)
        {
            return PatchAsync(key, model);
        }

        [HttpDelete]
        public Task<IActionResult> Delete(int key)
        {
            return DeleteAsync(key);
        }
    }
}
