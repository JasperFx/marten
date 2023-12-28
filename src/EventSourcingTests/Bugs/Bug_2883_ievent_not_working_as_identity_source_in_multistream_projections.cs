using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2883_ievent_not_working_as_identity_source : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2883_ievent_not_working_as_identity_source(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CanUseIEventAsSourceForIdentity()
    {
        StoreOptions(_ =>
        {
            _.Projections.Add<CustomerInsightsProjection>(ProjectionLifecycle.Inline);
        });

        var customersToCreate = 10;

        {
            await using var session = theStore.LightweightSession();
            session.Logger = new TestOutputMartenLogger(_output);

            for (var i = 0; i < customersToCreate; i++)
            {
                var stream = session.Events.StartStream(new CustomerCreated());
            }

            await session.SaveChangesAsync();
        }
        {
            await using var session = theStore.QuerySession();

            var docs = session.Query<CustomerInsightsResponse>().ToList();
            docs.Count.ShouldBeEquivalentTo(1);
            docs.First().NewCustomers.ShouldBe(customersToCreate);
        }
        var customersToDelete = 5;
        {
            await using var session = theStore.LightweightSession();
            session.Logger = new TestOutputMartenLogger(_output);

            for (var i = 0; i < customersToDelete; i++)
            {
                var stream = session.Events.StartStream(new CustomerDeleted());
            }

            await session.SaveChangesAsync();
        }
        {
            await using var session = theStore.QuerySession();

            var docs = session.Query<CustomerInsightsResponse>().ToList();
            docs.Count.ShouldBeEquivalentTo(1);
            docs.First().NewCustomers.ShouldBe(customersToCreate - customersToDelete);
        }
    }


    public class CustomerInsightsProjection : MultiStreamProjection<CustomerInsightsResponse, string>
{
    public CustomerInsightsProjection()
    {
        Identity<IEvent<CustomerCreated>>(x => DateOnly.FromDateTime(x.Timestamp.Date).ToString(CultureInfo.InvariantCulture));
        Identity<IEvent<CustomerDeleted>>(x => DateOnly.FromDateTime(x.Timestamp.Date).ToString(CultureInfo.InvariantCulture));
    }

    public CustomerInsightsResponse Create(IEvent<CustomerCreated> @event)
        => new(@event.Timestamp.Date.ToString(CultureInfo.InvariantCulture), DateOnly.FromDateTime(@event.Timestamp.DateTime), 1);

    public CustomerInsightsResponse Apply(IEvent<CustomerCreated> @event, CustomerInsightsResponse current)
        => current with { NewCustomers = current.NewCustomers + 1 };

    public CustomerInsightsResponse Apply(IEvent<CustomerDeleted> @event, CustomerInsightsResponse current)
        => current with { NewCustomers = current.NewCustomers - 1 };
}

public class CustomerDeleted
{
}

public record CustomerCreated();

public record CustomerInsightsResponse(string Id, DateOnly Date, int NewCustomers);
}
