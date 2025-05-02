using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core.Reflection;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace ContainerScopedProjectionTests;

public class when_registering_an_eventprojection_through_ioc_as_scoped: IAsyncLifetime
{
    private IHost _host;
    private IProjectionSource<IDocumentOperations,IQuerySession> source;
    private ProjectionBase basics;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";


                }).AddProjectionWithServices<LetterProjection2>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped,
                    x =>
                    {
                        x.Name = "Letters";
                        x.Version = 2;
                        x.IncludeType<AEvent>();
                        x.IncludeType<BEvent>();
                        x.IncludeType<CEvent>();
                        x.IncludeType<DEvent>();

                        x.FilterIncomingEventsOnStreamType(typeof(LetterCounts));

                        x.StreamType = typeof(LetterCounts);

                        x.Options.BatchSize = 111;

                        x.IncludeArchivedEvents = true;
                    });
            }).StartAsync();

        source = _host.DocumentStore().Options.As<StoreOptions>().Projections.All
            .Single(x => x.Name == "Letters");

        basics = source.As<ProjectionBase>();
    }

    public Task DisposeAsync()
    {
        return _host.StopAsync();
    }

    [Fact]
    public void projection_name()
    {
        source.Name.ShouldBe("Letters");
    }

    [Fact]
    public void projection_version()
    {
        source.Version.ShouldBe(2U);
    }

    [Fact]
    public void lifecycle()
    {
        source.Lifecycle.ShouldBe(ProjectionLifecycle.Inline);
    }

    [Fact]
    public void implementation_type()
    {
        source.ImplementationType.ShouldBe(typeof(LetterProjection2));
    }

    [Fact]
    public void async_options_come_through()
    {
        source.Options.BatchSize.ShouldBe(111);
    }

    [Fact]
    public void include_archived_events()
    {
        basics.IncludeArchivedEvents.ShouldBeTrue();
    }

    [Fact]
    public void has_the_stream_type_if_any()
    {
        basics.StreamType.ShouldBe(typeof(LetterCounts));
    }

    [Fact]
    public void included_event_types()
    {
        basics.IncludedEventTypes.ShouldContain(typeof(AEvent));
        basics.IncludedEventTypes.ShouldContain(typeof(BEvent));
        basics.IncludedEventTypes.ShouldContain(typeof(CEvent));
        basics.IncludedEventTypes.ShouldContain(typeof(DEvent));
    }

    [Fact]
    public async Task execute_end_to_end()
    {
        using var session = _host.DocumentStore().LightweightSession();
        session.Events.StartStream<LetterCounts>(new AEvent(), new BEvent());
        session.Events.StartStream<LetterCounts>(new AEvent(), new BEvent());
        session.Events.StartStream<LetterCounts>(new CEvent(), new BEvent());
        session.Events.StartStream<LetterCounts>(new BEvent(), new BEvent());
        session.Events.StartStream<LetterCounts>(new AEvent(), new AEvent());

        await session.SaveChangesAsync();
    }

    [Fact]
    public void categorized_as_event_projection()
    {
        source.Describe().SubscriptionType.ShouldBe(SubscriptionType.EventProjection);
    }
}
