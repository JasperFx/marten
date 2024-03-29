using System;
using System.Threading.Tasks;
using Bogus;
using Helpdesk.Api.Incidents;
using Helpdesk.Api.Tests.Incidents.Fixtures;
using Xunit;
using static Ogooreck.API.ApiSpecification;

namespace Helpdesk.Api.Tests.Incidents;

public class CategoriseIncidentTests: IClassFixture<ApiWithLoggedIncident>
{
    [Fact]
    [Trait("Category", "Acceptance")]
    public async Task CategoriseCommand_ChangesIncidentCategory()
    {
        await API
            .Given()
            .When(
                POST,
                URI($"/api/agents/{agentId}/incidents/{API.Incident.Id}/category"),
                BODY(new CategoriseIncidentRequest(category)),
                HEADERS(IF_MATCH(1))
            )
            .Then(OK);

        await API
            .Given()
            .When(GET, URI($"/api/incidents/{API.Incident.Id}"))
            .Then(
                OK,
                RESPONSE_BODY(
                    API.Incident with { Category = category, Version = 2 }
                )
            );
    }

    private readonly Guid agentId = Guid.NewGuid();
    private readonly IncidentCategory category = new Faker().PickRandom<IncidentCategory>();
    private readonly ApiWithLoggedIncident API;

    public CategoriseIncidentTests(ApiWithLoggedIncident api) => API = api;
}
