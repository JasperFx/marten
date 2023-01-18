using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;
using Shouldly;

namespace CoreTests;

public class MartenHost
{
    public static Task<IHost> For(Action<IServiceCollection> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((c, services) => configure(services))
            .StartAsync();
    }
}

public class StubInitialData: IInitialData
{
    public Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        ReceivedStore = store;
        return Task.CompletedTask;
    }

    public IDocumentStore ReceivedStore { get; set; }
}

public class working_with_initial_data : OneOffConfigurationsContext
{
    [Fact]
    public async Task runs_all_the_initial_data_sets_on_startup()
    {
        var data1 = Substitute.For<IInitialData>();
        var data2 = Substitute.For<IInitialData>();
        var data3 = Substitute.For<IInitialData>();

        using var host = await MartenHost.For(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);

                })
                .InitializeWith(data1, data2, data3);
        });

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.InitialData.ShouldHaveTheSameElementsAs(data1, data2, data3);

        await data1.Received().Populate(store, Arg.Any<CancellationToken>());
        await data2.Received().Populate(store, Arg.Any<CancellationToken>());
        await data3.Received().Populate(store, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task runs_all_the_initial_data_sets_on_startup_2()
    {
        var data1 = Substitute.For<IInitialData>();
        var data2 = Substitute.For<IInitialData>();
        var data3 = Substitute.For<IInitialData>();

        using var host = await MartenHost.For(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);

            });

            services.InitializeMartenWith(data1, data2, data3);

        });

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.InitialData.ShouldHaveTheSameElementsAs(data1, data2, data3);

        await data1.Received().Populate(store, Arg.Any<CancellationToken>());
        await data2.Received().Populate(store, Arg.Any<CancellationToken>());
        await data3.Received().Populate(store, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task use_service_registration_for_initial_data()
    {
        using var host = await MartenHost.For(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);

                })
                .InitializeWith<StubInitialData>();
        });

        var stub = host.Services.GetServices<IInitialData>().OfType<StubInitialData>().Single();
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        stub.ReceivedStore.ShouldBe(store);
    }


    [Fact]
    public async Task use_service_registration_for_initial_data_separate_registration()
    {
        using var host = await MartenHost.For(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);

            });

            services.InitializeMartenWith<StubInitialData>();

        });

        var stub = host.Services.GetServices<IInitialData>().OfType<StubInitialData>().Single();
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        stub.ReceivedStore.ShouldBe(store);
    }

    public interface IOtherStore : IDocumentStore{}

    [Fact]
    public async Task runs_all_the_initial_data_sets_on_startup_on_other_store()
    {
        var data1 = Substitute.For<IInitialData>();
        var data2 = Substitute.For<IInitialData>();
        var data3 = Substitute.For<IInitialData>();

        using var host = await MartenHost.For(services =>
        {
            services.AddMartenStore<IOtherStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);

                })
                .InitializeWith(data1, data2, data3);
        });

        var store = host.Services.GetRequiredService<IOtherStore>().As<DocumentStore>();
        store.Options.InitialData.ShouldHaveTheSameElementsAs(data1, data2, data3);

        await data1.Received().Populate(store, Arg.Any<CancellationToken>());
        await data2.Received().Populate(store, Arg.Any<CancellationToken>());
        await data3.Received().Populate(store, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task runs_all_the_initial_data_sets_on_startup_on_other_store_separate()
    {
        var data1 = Substitute.For<IInitialData>();
        var data2 = Substitute.For<IInitialData>();
        var data3 = Substitute.For<IInitialData>();

        using var host = await MartenHost.For(services =>
        {
            services.AddMartenStore<IOtherStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);

            });

            services.InitializeMartenWith<IOtherStore>(data1, data2, data3);
        });

        var store = host.Services.GetRequiredService<IOtherStore>().As<DocumentStore>();
        store.Options.InitialData.ShouldHaveTheSameElementsAs(data1, data2, data3);

        await data1.Received().Populate(store, Arg.Any<CancellationToken>());
        await data2.Received().Populate(store, Arg.Any<CancellationToken>());
        await data3.Received().Populate(store, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task use_service_registration_for_initial_data_for_other_store()
    {
        using var host = await MartenHost.For(services =>
        {
            services.AddMartenStore<IOtherStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);

                })
                .InitializeWith<StubInitialData>();
        });

        var store = host.Services.GetRequiredService<IOtherStore>().As<DocumentStore>();
        var stub = store.Options.InitialData.OfType<StubInitialData>().Single();
        stub.ReceivedStore.ShouldBe(store);
    }


    [Fact]
    public async Task use_service_registration_for_initial_data_for_other_store_separate_call()
    {
        using var host = await MartenHost.For(services =>
        {
            services.AddMartenStore<IOtherStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);

            });

            services
                .InitializeMartenWith<IOtherStore, StubInitialData>();
        });

        var store = host.Services.GetRequiredService<IOtherStore>().As<DocumentStore>();
        var stub = store.Options.InitialData.OfType<StubInitialData>().Single();
        stub.ReceivedStore.ShouldBe(store);
    }


    [Fact]
    public async Task use_service_registration_for_initial_data_for_other_store_2()
    {
        using var host = await MartenHost.For(services =>
        {
            services.AddMartenStore<IOtherStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);

            });

            services.InitializeMartenWith<IOtherStore, StubInitialData>();
        });

        var store = host.Services.GetRequiredService<IOtherStore>().As<DocumentStore>();
        var stub = store.Options.InitialData.OfType<StubInitialData>().Single();
        stub.ReceivedStore.ShouldBe(store);
    }



    [Fact]
    public async Task initial_data_should_populate_db_with_query_in_populate_method()
    {
        var store = DocumentStore.For(_ =>
        {
            _.DatabaseSchemaName = "Bug1495";

            _.Connection(ConnectionSource.ConnectionString);

            _.InitialData.Add(new InitialDataWithQuery(InitialWithQueryDatasets.Aggregates));
        });

        await store.Advanced.ResetAllData();

        await using (var session = store.QuerySession())
        {
            foreach (var initialAggregate in InitialWithQueryDatasets.Aggregates)
            {
                var aggregate = session.Query<Aggregate1495>().First(x => x.Name == initialAggregate.Name);
                aggregate.Name.ShouldBe(initialAggregate.Name);
            }
        }

        store.Dispose();
    }
}
public class InitialDataWithQuery: IInitialData
{
    private readonly Aggregate1495[] _initialData;

    public InitialDataWithQuery(params Aggregate1495[] initialData)
    {
        _initialData = initialData;
    }

    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = await store.LightweightSessionAsync(token: cancellation);
        if (!session.Query<Aggregate1495>().Any())
        {
            session.Store(_initialData);
            await session.SaveChangesAsync(cancellation);
        }
    }
}

public static class InitialWithQueryDatasets
{
    public static readonly Aggregate1495[] Aggregates =
    {
        new Aggregate1495 { Name = "Aggregate 1" },
        new Aggregate1495 { Name = "Aggregate 2" }
    };
}

public class Aggregate1495
{
    public Aggregate1495()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public string Name { get; set; }
}
