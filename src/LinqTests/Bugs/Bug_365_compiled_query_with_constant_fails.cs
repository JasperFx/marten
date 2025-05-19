using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_365_compiled_query_with_constant_fails: BugIntegrationContext
{
    public class Route
    {
        public Guid ID { get; set; }
        public DateTime Date { get; private set; }
        public RouteStatus Status { get; private set; }

        public void Plan(DateTime date)
        {
            if (date < DateTime.Today.AddDays(1))
            {
                throw new InvalidOperationException("Route can only plan from tomorrow.");
            }

            Status = RouteStatus.Planned;
            Date = date;
        }
    }

    public enum RouteStatus
    {
        Created,
        Planned,
        Driving,
        Stopped
    }

    public Bug_365_compiled_query_with_constant_fails()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Route>();
        });

        theStore.Options.Providers.StorageFor<Route>().ShouldNotBeNull();
    }

    public class RoutesPlannedAfter: ICompiledQuery<Route, IEnumerable<Route>>
    {
        public DateTime DateTime { get; }

        public RoutesPlannedAfter(DateTime dateTime)
        {
            DateTime = dateTime;
        }

        public Expression<Func<IMartenQueryable<Route>, IEnumerable<Route>>> QueryIs()
        {
            return query => query.Where(route => route.Status == RouteStatus.Planned && route.Date > DateTime);
        }
    }

    [Fact]
    public async Task Index_was_outside_the_bounds_of_the_array()
    {
        await AddRoutes(30);

        var from = DateTime.Today.AddDays(5);

        using (var session = theStore.QuerySession())
        {
            var routes = await session.QueryAsync(new RoutesPlannedAfter(from));
            var all = session.Query<Route>();

            routes.Count().ShouldBe(all.Count(route => route.Status == RouteStatus.Planned && route.Date > from));
        }
    }

    private async Task AddRoutes(int number)
    {
        using var session = theStore.LightweightSession();
        for (var index = 0; index < number; index++)
        {
            var route = new Route();
            if (index % 2 == 0)
            {
                route.Plan(DateTime.Today.AddDays(index + 1));
            }

            session.Store(route);
        }

        await session.SaveChangesAsync();
    }
}
