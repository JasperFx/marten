using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class determining_the_event_store_identity
{
    [Fact]
    public async Task use_correct_identities()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "es_identity";
                });

                services.AddMartenStore<IThingStore>(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "things";
                });

            }).StartAsync();

        var stores = host.Services.GetServices<IEventStore>().ToArray();
        stores.Single(x => x.GetType() == typeof(DocumentStore)).As<IEventStore>().Identity.ShouldBe(new EventStoreIdentity("main", "marten"));
        stores.OfType<IThingStore>().Single().As<IEventStore>().Identity.ShouldBe(new EventStoreIdentity("ithingstore", "marten"));
    }
}

public interface IThingStore: IDocumentStore;
