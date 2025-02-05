using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using Lamar;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2438_generated_code_throwing_nre
{
    [Fact]
    public async Task should_not_fail_on_second_time()
    {
        await execute_projection();
        await execute_projection();
    }

    protected async Task execute_projection()
    {
        await using var container = new Container(services =>
        {
            services.AddMarten(options =>
            {
                options.Connection(ConnectionSource.ConnectionString);

                options.CreateDatabasesForTenants(c =>
                {
                    c.ForTenant()
                        .CheckAgainstPgDatabase()
                        .WithEncoding("UTF-8")
                        .ConnectionLimit(-1);
                });

                options.Projections.Add<MyEventProjection>(ProjectionLifecycle.Inline);
                options.AutoCreateSchemaObjects = AutoCreate.All;
                options.GeneratedCodeMode = TypeLoadMode.Auto;
                options.SetApplicationProject(typeof(MyEventProjection).Assembly);
                options.SourceCodeWritingEnabled = true;
            });
        });

        var store = container.GetInstance<IDocumentStore>();
        await using var session = store.LightweightSession();

        session.Events.StartStream(new MyEvent(Guid.NewGuid(), "Stuff"));
        await session.SaveChangesAsync();
    }

    public record MyEvent(Guid Id, string Stuff);

    public class MyEventProjection: EventProjection
    {
        public MyEventProjection()
        {
            Project<MyEvent>((@event, operations) =>
            {
            });
        }
    }
}
