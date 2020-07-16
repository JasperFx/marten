using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.SchemaChange
{
    // Different namespace - event will be stored with "Old"
    namespace New
    {
        public class TaskCreated
        {
            public Guid TaskId { get; }
            public string Description { get; }

            public TaskCreated(Guid taskId, string description)
            {
                TaskId = taskId;
                Description = description;
            }
        }

        // Type name has changed - Event will be stored with `TaskDescriptionUpdated`
        public class TaskDescriptionUpdatedNewName
        {
            public Guid TaskId { get; }
            public string Description { get; }

            public TaskDescriptionUpdatedNewName(Guid taskId, string description)
            {
                TaskId = taskId;
                Description = description;
            }
        }


        public class Task
        {
            public Guid Id { get; private set; }
            public string Description { get; private set; }

            public void Apply(TaskCreated @event)
            {
                Id = @event.TaskId;
                Description = @event.Description;
            }

            public void Apply(TaskDescriptionUpdatedNewName @event)
            {
                Description = @event.Description;
            }
        }
    }

    [Collection("events_namespace_migration")]
    public class EventsNamespaceChange: OneOffConfigurationsContext
    {
        [Fact]
        public async Task HavingEvents_WithSchemaChange_AggregationShouldWork()
        {
            // test events data
            var taskId = Guid.NewGuid();
            const string initialDescription = "initial description";
            const string updatedDescription = "updated description";

            // events types
            const string taskCreatedType = "task_created";
            const string taskDescriptionUpdatedType = "task_description_updated";

            // simulate namespace change from `Old` to `New`
            const string oldTaskCreatedClrType = "Marten.Testing.Events.SchemaChange.Old.TaskCreated, Marten.Testing";
            // simulate namespace change from `TaskDescriptionUpdated` to `TaskDescriptionUpdatedNewName`
            const string oldTaskUpdatedClrType = "Marten.Testing.Events.SchemaChange.Old.TaskDescriptionUpdated, Marten.Testing";

            using (var session = theStore.OpenSession())
            {
                // ensure events tables already exists
                session.Tenant.EnsureStorageExists(typeof(EventStream));

                using (var conn = session.Tenant.OpenConnection())
                {
                    // we need to insert data manually, as if we keep old type in assembly then Marten would use it.
                    // Marten at first tries to find concrete type based on the `mt_dotnet_type` column value.
                    // When Marten cannot find concrete CLR type then it searches it by `type` column value
                    conn.Execute(
                        $@"INSERT INTO events_namespace_migration.mt_streams (id, type, version, timestamp, snapshot, snapshot_version, created, tenant_id)
                        VALUES ('{taskId}', null, 2, '2020-07-16 19:32:43.912171', null, null, '2020-07-16 19:32:43.912171', '*DEFAULT*');");
                    conn.Execute("INSERT INTO events_namespace_migration.mt_events (seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type) " +
                        $"VALUES (1, '{Guid.NewGuid()}', '{taskId}', 1, '{{\"TaskId\": \"{taskId}\", \"Description\": \"{initialDescription}\"}}', '{taskCreatedType}', '2020-07-16 19:32:43.912171', '*DEFAULT*', '{oldTaskCreatedClrType}');");
                    conn.Execute("INSERT INTO events_namespace_migration.mt_events (seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type)" +
                        $"VALUES (2, '{Guid.NewGuid()}', '{taskId}', 2, '{{\"TaskId\": \"{taskId}\", \"Description\": \"{updatedDescription}\"}}', '{taskDescriptionUpdatedType}', '2020-07-16 19:32:43.912171', '*DEFAULT*', '{oldTaskUpdatedClrType}');");
                }

                await session.SaveChangesAsync();
            }

            using (var store = SeparateStore(_ =>
            {
                // Add new Event types, if type names won't change then the same type name will be generated
                // and we don't need additional config
                _.Events.AddEventTypes(new []{typeof(New.TaskCreated), typeof(New.TaskDescriptionUpdatedNewName)});

                // When type name has changed we need to define custom mapping
                _.Events.EventMappingFor<New.TaskDescriptionUpdatedNewName>()
                        .EventTypeName = taskDescriptionUpdatedType;
            }))
            {
                using (var session = store.OpenSession())
                {
                    var taskNew = await session.Events.AggregateStreamAsync<New.Task>(taskId);

                    taskNew.Id.ShouldBe(taskId);
                    taskNew.Description.ShouldBe(updatedDescription);
                }
            }
        }

        public EventsNamespaceChange() : base("events_namespace_migration")
        {
        }
    }
}
