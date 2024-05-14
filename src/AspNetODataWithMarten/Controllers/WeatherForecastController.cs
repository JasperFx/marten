using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace AspNetODataWithMarten.Controllers;

[Route("/weatherforecast")]
public class WeatherForecastController : ODataController
{
    [HttpGet]
    [EnableQuery]
    public IActionResult Get([FromServices] IDocumentSession session )
    {
        return Ok(session.Query<WeatherForecast>());
    }
}
