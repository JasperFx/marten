using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Lamar.Microsoft.DependencyInjection;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3080_WaitForNonStaleData_should_work_dammit
{
    [Fact]
    public async Task WaitForNonStaleProjectionDataAsync_does_not_work()
    {
        using var host = await Setup();

        // Seed Data on one tenant
        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession("tenant2");

        // Start stream with a bunch of events
        var id = Guid.NewGuid();
        var created = new MyAggregateCreated(id, "Initial");
        var updated1 = new MyAggregateUpdated(id, "Updated 1");
        var updated2 = new MyAggregateUpdated(id, "Updated 2");

        session.Events.StartStream<MyAggregate>(id, created, updated1, updated2);
        await session.SaveChangesAsync();

        // Append a few more
        var updated3 = new MyAggregateUpdated(id, "Updated 3");
        var updated4 = new MyAggregateUpdated(id, "Updated 4");
        session.Events.Append(id, updated3, updated4);
        await session.SaveChangesAsync();


        // Wait for table projection
        await store.WaitForNonStaleProjectionDataAsync("tenant2",TimeSpan.FromSeconds(30));

        //await Task.Delay(2000);

        var names = await session.QueryAsync<string>("select name from mt_tbl_my_aggregate where id = ?", id);

        Assert.Single(names);
        Assert.Equal("Updated 4", names[0]);
    }

    private static async Task<IHost> Setup()
    {
        var masterConnectionString = ConnectionSource.ConnectionString;
        string[] tenantIds =
        [
            "tenant1",
            "tenant2",
            "tenant3"
        ];

        await ResetDatabasesAsync(masterConnectionString, tenantIds);

        var host = await Host.CreateDefaultBuilder()
            .UseLamar(services =>
            {
                var marten = services.AddMarten(ops =>
                {
                    ops.DatabaseSchemaName = "wait";

                    ops.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                    {
                        x.ConnectionString = masterConnectionString;
                        x.SchemaName = "tenants";
                        x.AutoCreate = AutoCreate.CreateOrUpdate;

                        x.RegisterDatabase("tenant1", CreateConnectionString(masterConnectionString, "tenant1"));
                        x.RegisterDatabase("tenant2", CreateConnectionString(masterConnectionString, "tenant2"));
                        x.RegisterDatabase("tenant3", CreateConnectionString(masterConnectionString, "tenant3"));
                    });

                    ops.Projections.LiveStreamAggregation<MyAggregate>();
                    ops.Projections.Add<MyAggregateTableProjection>(ProjectionLifecycle.Async);

                    // Really needed?
                    ops.AutoCreateSchemaObjects = AutoCreate.All;
                });

                marten.AddAsyncDaemon(DaemonMode.Solo);
                marten.ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        return host;

        static async Task ResetDatabasesAsync(string masterConnectionString, string[] tenantIds)
        {
            await using var connection = new NpgsqlConnection(masterConnectionString);
            await connection.OpenAsync();

            // Drop master table tenancy schema
            await connection.DropSchemaAsync("tenants");

            // (Re)-create tenant databases
            foreach (var tenantId in tenantIds)
            {
                var exists = await connection.DatabaseExists(tenantId);
                if (exists)
                {
                    using var tenantConn =
                        new NpgsqlConnection(CreateConnectionString(ConnectionSource.ConnectionString, tenantId));
                    await tenantConn.OpenAsync();
                    await tenantConn.DropSchemaAsync("wait");
                    await tenantConn.CloseAsync();

                    return;
                }

                await new DatabaseSpecification().BuildDatabase(connection, tenantId);
            }
        }

        static string CreateConnectionString(string masterConnectionString, string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder(masterConnectionString)
            {
                Database = databaseName,
                PersistSecurityInfo = true
            };

            return builder.ConnectionString;
        }
    }

    public record MyAggregateCreated(Guid Id, string Name);

    public record MyAggregateUpdated(Guid Id, string Name);

    public record MyAggregateDeleted(Guid Id);

    public record MyAggregate(Guid Id, string Name)
    {
        public static MyAggregate Create(MyAggregateCreated @event)
            => new(@event.Id, @event.Name);

        public MyAggregate Apply(MyAggregateUpdated @event)
            => this with { Name = @event.Name };
    }

    public class MyAggregateTableProjection: EventProjection
    {
        private readonly string _tableName;

        public MyAggregateTableProjection()
        {
            ProjectionName = "MyProjection";

            var table = new Table("mt_tbl_my_aggregate");

            table.AddColumn<Guid>("id");
            table.AddColumn<string>("name");

            SchemaObjects.Add(table);
            _tableName = table.Identifier.ToString();
        }

        public void Project(IEvent<MyAggregateCreated> @event, IDocumentOperations ops)
        {
            ops.QueueSqlCommand(
                $"""
                 insert into {_tableName}(id, name) values (?, ?)
                 """,
                @event.Data.Id,
                @event.Data.Name);
        }

        public void Project(IEvent<MyAggregateUpdated> @event, IDocumentOperations ops)
        {
            ops.QueueSqlCommand(
                $"""
                 update {_tableName} set name = ? where id = ?
                 """,
                @event.Data.Name,
                @event.Data.Id);
        }

        public void Project(IEvent<MyAggregateDeleted> @event, IDocumentOperations ops)
        {
            ops.QueueSqlCommand(
                $"""
                 delete from {_tableName} where id = ?
                 """,
                @event.Data.Id);
        }
    }
}
