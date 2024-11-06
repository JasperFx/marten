using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace StressTests.Bugs;

public sealed class Bug_3113_do_not_reorder_sql_operations : BugIntegrationContext
{
    [Fact]
    public Task does_not_reorder_sql_commands_randomly_single_document_projections()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<Thing>();
            opts.Projections.Snapshot<MyProjection1>(SnapshotLifecycle.Inline);
            opts.Projections.Add<MyTableProjection>(ProjectionLifecycle.Inline);
        });

        return run_test();
    }

    [Fact]
    public Task does_not_reorder_sql_commands_randomly_multiple_document_projections()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<Thing>();
            opts.Projections.Snapshot<MyProjection1>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<MyProjection2>(SnapshotLifecycle.Inline);
            opts.Projections.Add<MyTableProjection>(ProjectionLifecycle.Inline);
        });

        return run_test();
    }

    private async Task run_test()
    {
        await using var session = theStore.LightweightSession();

        var thingId1 = AddThing("First");
        var thingId2 = AddThing("Second");

        await session.SaveChangesAsync();

        var thingUsers1 = AssignUsers(thingId1, 2);
        var thingUsers2 = AssignUsers(thingId2, 20);

        await session.SaveChangesAsync();

        var actualThingUsers1 = await ReadUserIdsAsync(thingId1);
        actualThingUsers1.ShouldBe(thingUsers1);

        var actualThingUsers2 = await ReadUserIdsAsync(thingId2);
        actualThingUsers2.ShouldBe(thingUsers2);

        Guid AddThing(string name)
        {
            var id = Guid.NewGuid();
            var created = new ThingCreated(id, name);
            session.Events.StartStream<Thing>(created.Id, created);

            return id;
        }

        IEnumerable<Guid> AssignUsers(Guid thingId, int count)
        {
            var users = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
            var assigned = new ThingUsersAssigned(thingId, users);
            session.Events.Append(assigned.Id, assigned);

            return users;
        }

        async Task<IReadOnlyList<Guid>> ReadUserIdsAsync(Guid thingId)
        {
            return await session.QueryAsync<Guid>($"select user_id from {MyTableProjection.UsersTableName} where id = ?", thingId);
        }
    }

    public record ThingCreated(Guid Id, string Name);
    public record ThingUsersAssigned(Guid Id, IEnumerable<Guid> UserIds);

    public record Thing(Guid Id, string Name)
    {
        public static Thing Create(ThingCreated @event)
            => new Thing(@event.Id, @event.Name);
    }

    [DocumentAlias("projection1")]
    public record MyProjection1(Guid Id, string Name, IEnumerable<Guid> UserIds)
    {
        public static MyProjection1 Create(ThingCreated @event)
            => new(@event.Id, @event.Name, []);

        public MyProjection1 Apply(ThingUsersAssigned @event)
            => this with
            {
                UserIds = @event.UserIds
            };
    }

    [DocumentAlias("projection2")]
    public record MyProjection2(Guid Id, IEnumerable<Guid> UserIds)
    {
        public static MyProjection2 Create(ThingCreated @event)
            => new(@event.Id, []);

        public MyProjection2 Apply(ThingUsersAssigned @event)
            => this with
            {
                UserIds = @event.UserIds
            };
    }

    public class MyTableProjection : EventProjection
    {
        public const string MainTableName = "mt_tbl_bug_3113";
        public const string UsersTableName = $"{MainTableName}_users";

        public MyTableProjection()
        {
            var mainTable = new Table(MainTableName);

            mainTable.AddColumn<Guid>("id").AsPrimaryKey();
            mainTable.AddColumn<string>("name").AsPrimaryKey();

            SchemaObjects.Add(mainTable);

            var usersTable = new Table(UsersTableName);

            usersTable.AddColumn<Guid>("id").AsPrimaryKey();
            usersTable.AddColumn<Guid>("user_id").AsPrimaryKey();

            SchemaObjects.Add(usersTable);

            foreach (var table in SchemaObjects.OfType<Table>())
            {
                Options.DeleteDataInTableOnTeardown(table.Identifier);
            }
        }

        public void Project(IEvent<ThingCreated> @event, IDocumentOperations ops)
        {
            ops.QueueSqlCommand(
                $"""
                insert into {MainTableName} (id, name) values (?, ?)
                """,
                @event.Data.Id,
                @event.Data.Name);
        }

        public void Project(IEvent<ThingUsersAssigned> @event, IDocumentOperations ops)
        {
            ops.QueueSqlCommand(
                $"""
                delete from {UsersTableName} where id = ?
                """,
                @event.Data.Id);

            foreach (var userId in @event.Data.UserIds)
            {
                ops.QueueSqlCommand(
                    $"""
                    insert into {UsersTableName} (id, user_id) values (?, ?)
                    """,
                    @event.Data.Id,
                    userId);
            }
        }
    }
}
