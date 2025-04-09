using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.MultiTenancy;

public class multi_tenancy_by_database: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IHost _host;
    private IDocumentStore theStore;

    public multi_tenancy_by_database(ITestOutputHelper output)
    {
        _output = output;
        Logger = new TestLogger<IProjection>(output);
    }

    public TestLogger<IProjection> Logger { get; set; }

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        await using var dbConn = new NpgsqlConnection(builder.ConnectionString);
        await dbConn.OpenAsync();
        await dbConn.DropSchemaAsync("multi_tenancy_daemon");

        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1ConnectionString = await CreateDatabaseIfNotExists(conn, "database1");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");


        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogger<IProjection>>(Logger);

                services.AddMarten(opts =>
                {
                    opts.DatabaseSchemaName = "multi_tenancy_daemon";

                    opts
                        .MultiTenantedWithSingleServer(
                            ConnectionSource.ConnectionString,
                            t =>
                                t.WithTenants("tenant1").InDatabaseNamed("database1")
                                    .WithTenants("tenant3", "tenant4") // own database
                        );


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
    public void fail_when_trying_to_create_daemon_with_no_tenant_sync()
    {
        Should.Throw<DefaultTenantUsageDisabledException>(async () =>
        {
            await theStore.BuildProjectionDaemonAsync();
        });
    }

    [Fact]
    public async Task build_daemon_for_database()
    {
        using var daemon = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync("tenant1");

        daemon.Database.Identifier.ShouldBe("database1");

        await using var conn = daemon.Database.As<IMartenDatabase>().CreateConnection();
        conn.Database.ShouldBe("database1");
    }


    [Fact]
    public async Task build_daemon_for_database_async()
    {
        using var daemon = (ProjectionDaemon)(await theStore.BuildProjectionDaemonAsync("tenant1"));

        daemon.Database.Identifier.ShouldBe("database1");

        await using var conn = daemon.Database.As<IMartenDatabase>().CreateConnection();
        conn.Database.ShouldBe("database1");
    }

    [Fact]
    public async Task run_projections_end_to_end()
    {
        var id = Guid.NewGuid();

        await using var session1 = theStore.LightweightSession("tenant1");
        session1.Events.Append(id, new AEvent(), new BEvent(), new BEvent());
        await session1.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession("tenant3");
        session3.Events.Append(id, new AEvent(), new AEvent(), new BEvent(), new BEvent());
        await session3.SaveChangesAsync();

        await using var session4 = theStore.LightweightSession("tenant4");
        session4.Events.Append(id, new AEvent(), new BEvent(), new BEvent(), new BEvent());
        await session4.SaveChangesAsync();

        await (await theStore.Storage.FindOrCreateDatabase("tenant1")).Tracker.WaitForShardState("AllGood:All", 3);
        await (await theStore.Storage.FindOrCreateDatabase("tenant3")).Tracker.WaitForShardState("AllGood:All", 4);
        await (await theStore.Storage.FindOrCreateDatabase("tenant4")).Tracker.WaitForShardState("AllGood:All", 4);

        (await session1.LoadAsync<MyAggregate>(id)).ShouldBe(new MyAggregate { Id = id, ACount = 1, BCount = 2 });
        (await session3.LoadAsync<MyAggregate>(id)).ShouldBe(new MyAggregate { Id = id, ACount = 2, BCount = 2 });
        (await session4.LoadAsync<MyAggregate>(id)).ShouldBe(new MyAggregate { Id = id, ACount = 1, BCount = 3 });
    }
}

public class AllSync: SingleStreamProjection<MyAggregate, Guid>
{
    public AllSync()
    {
        ProjectionName = "AllSync";
    }

    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate { ACount = @event.A, BCount = @event.B, CCount = @event.C, DCount = @event.D };
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

public class AllGood: SingleStreamProjection<MyAggregate, Guid>
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
        return new MyAggregate { ACount = @event.A, BCount = @event.B, CCount = @event.C, DCount = @event.D };
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

    protected bool Equals(MyAggregate other)
    {
        return Id.Equals(other.Id) && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount &&
               DCount == other.DCount && ECount == other.ECount;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((MyAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, ACount, BCount, CCount, DCount, ECount);
    }

    public override string ToString()
    {
        return
            $"{nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}, {nameof(ECount)}: {ECount}";
    }
}

public interface ITabulator
{
    void Apply(MyAggregate aggregate);
}

public class AEvent: ITabulator
{
    // Necessary for a couple tests. Let it go.
    public Guid Id { get; set; }

    public void Apply(MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public Guid Tracker { get; } = Guid.NewGuid();
}

public class BEvent: ITabulator
{
    public void Apply(MyAggregate aggregate)
    {
        aggregate.BCount++;
    }
}

public class CEvent: ITabulator
{
    public void Apply(MyAggregate aggregate)
    {
        aggregate.CCount++;
    }
}

public class DEvent: ITabulator
{
    public void Apply(MyAggregate aggregate)
    {
        aggregate.DCount++;
    }
}

public class EEvent
{
}

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
