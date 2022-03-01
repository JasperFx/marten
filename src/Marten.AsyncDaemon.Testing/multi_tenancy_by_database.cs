using System;
using System.Threading.Tasks;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{


    public class multi_tenancy_by_database : IAsyncLifetime
    {
        private IHost _host;
        private IDocumentStore theStore;

        public async Task InitializeAsync()
        {
            _host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts
                            .MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                            .WithTenants("tenant1", "tenant2").InDatabaseNamed("database1")
                            .WithTenants("tenant3", "tenant4"); // own database


                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                        opts.Projections.Add<AllGood>(ProjectionLifecycle.Async);

                    }).ApplyAllDatabaseChangesOnStartup().AddAsyncDaemon(DaemonMode.Solo);
                }).StartAsync();

            theStore = _host.Services.GetRequiredService<IDocumentStore>();
        }

        public Task DisposeAsync()
        {
            return _host.StopAsync();
        }

        [Fact]
        public async Task fail_when_trying_to_create_daemon_with_no_tenant()
        {
            await Should.ThrowAsync<DefaultTenantUsageDisabledException>(async () =>
            {
                await theStore.BuildProjectionDaemonAsync();
            });
        }

        [Fact]
        public async Task fail_when_trying_to_create_daemon_with_no_tenant_sync()
        {
            Should.Throw<DefaultTenantUsageDisabledException>(() =>
            {
                theStore.BuildProjectionDaemon();
            });
        }

        [Fact]
        public async Task build_daemon_for_database()
        {
            using var daemon = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync("tenant1");

            daemon.Database.Identifier.ShouldBe("database1");

            using var conn = daemon.Database.CreateConnection();
            conn.Database.ShouldBe("database1");
        }


        [Fact]
        public void build_daemon_for_database_sync()
        {
            using var daemon = (ProjectionDaemon)theStore.BuildProjectionDaemon("tenant1");

            daemon.Database.Identifier.ShouldBe("database1");

            using var conn = daemon.Database.CreateConnection();
            conn.Database.ShouldBe("database1");
        }
    }

        public class AllSync: AggregateProjection<MyAggregate>
    {
        public AllSync()
        {
            ProjectionName = "AllSync";
        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount,
                Id = aggregate.Id
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1,
                Id = aggregate.Id
            };
        }
    }

    public class AllGood: AggregateProjection<MyAggregate>
    {
        public AllGood()
        {
            ProjectionName = "AllGood";
        }

        [MartenIgnore]
        public void RandomMethodName()
        {

        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public Task<MyAggregate> Create(CreateEvent @event, IQuerySession session)
        {
            return null;
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount,
                Id = aggregate.Id
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1,
                Id = aggregate.Id
            };
        }
    }

    public class MyAggregate
    {
        public Guid Id { get; set; }

        public int ACount { get; set; }
        public int BCount { get; set; }
        public int CCount { get; set; }
        public int DCount { get; set; }
        public int ECount { get; set; }

        public string Created { get; set; }
        public string UpdatedBy { get; set; }
        public Guid EventId { get; set; }
    }

    public interface ITabulator
    {
        void Apply(MyAggregate aggregate);
    }

    public class AEvent : ITabulator
    {
        // Necessary for a couple tests. Let it go.
        public Guid Id { get; set; }

        public void Apply(MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public Guid Tracker { get; } = Guid.NewGuid();
    }

    public class BEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.BCount++;
        }
    }

    public class CEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.CCount++;
        }
    }

    public class DEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.DCount++;
        }
    }
    public class EEvent {}

    public class CreateEvent
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public CreateEvent(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }
}
