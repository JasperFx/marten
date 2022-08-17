using System;
using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Marten.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace IssueService.Controllers
{
    public class ThingController : ControllerBase
    {
        [HttpGet("/things/{tenantId}")]
        public Task GetThings(string tenantId, [FromServices] IDocumentStore store)
            => store.LightweightSession(tenantId).Query<Thing>().WriteArray(HttpContext);

    }

    public class Thing : ITenanted
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string TenantId { get; set; }
    }
}
