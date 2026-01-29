using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using EventSourcingTests.Projections.EventProjections;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class propagate_logger_to_projections
{
    [Fact]
    public async Task loggers_exist_on_projections()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "system_part";

                    m.Projections.Add<SampleEventProjection>(ProjectionLifecycle.Inline);
                    m.Projections.Add<TestOrderingEventProjection>(ProjectionLifecycle.Inline);
                });
            }).StartAsync();

        var store = host.DocumentStore();
        var projections = store.Options.As<StoreOptions>().Projections.All;
        projections.Any().ShouldBeTrue();

        foreach (var projection in projections)
        {
            var expectedType = typeof(ILogger<>).MakeGenericType(projection.GetType());
            projection.As<IHasLogger>().Logger.GetType().CanBeCastTo(expectedType);
        }
    }
}
