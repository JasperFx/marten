using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace IssueService.Controllers
{
    public class JsonController : ControllerBase
    {
        [HttpGet("/json/sql/{value1}/{value2}")]
        public Task GetJsonFromSql([FromServices] IQuerySession store, string value1, string value2)
            => store.WriteJson("SELECT jsonb_build_object('Property', ?) UNION SELECT jsonb_build_object('Property', ?);", HttpContext, contentType: "application/json", onFoundStatus: 200, parameters: new object[]{value1, value2});

    }
}
