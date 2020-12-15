using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.SchemaChange
{
    // SAMPLE: old_event_namespace
    namespace OldEventNamespace
    {
        public class OrderStatusChanged
        {
            public Guid OrderId { get; }
            public int Status { get; }

            public OrderStatusChanged(Guid orderId, int status)
            {
                OrderId = orderId;
                Status = status;
            }
        }
    }
    // ENDSAMPLE

    // SAMPLE: new_event_namespace
    namespace NewEventNamespace
    {
        public class OrderStatusChanged
        {
            public Guid OrderId { get; }
            public int Status { get; }

            public OrderStatusChanged(Guid orderId, int status)
            {
                OrderId = orderId;
                Status = status;
            }
        }
    }
    // ENDSAMPLE


    // SAMPLE: new_event_type_name
    namespace OldEventNamespace
    {
        public class ConfirmedOrderStatusChanged
        {
            public Guid OrderId { get; }
            public int Status { get; }

            public ConfirmedOrderStatusChanged(Guid orderId, int status)
            {
                OrderId = orderId;
                Status = status;
            }
        }
    }
    // ENDSAMPLE

    public static class SampleEventsSchemaMigration
    {
        public static void SampleAddEventsRegistration()
        {
            // SAMPLE: event_namespace_migration_options
            var options = new StoreOptions();

            options.Events.AddEventTypes(new[] {typeof(NewEventNamespace.OrderStatusChanged)});

            var store = new DocumentStore(options);
            // ENDSAMPLE
        }

        public static void SampleEventMappingRegistration()
        {
            // SAMPLE: event_type_name_migration_options
            var options = new StoreOptions();

            var orderStatusChangedMapping = options.Events.EventMappingFor<OldEventNamespace.ConfirmedOrderStatusChanged>();
            orderStatusChangedMapping.EventTypeName = "order_status_changed";

            var store = new DocumentStore(options);
            // ENDSAMPLE
        }
    }

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

            var oldTaskCreatedEvent = new
            {
                Id = Guid.NewGuid(),
                StreamId = taskId,
                EventTypeName = "task_created",

                // simulate namespace change from `Old` to `New`
                DotnetTypeName = "Marten.Testing.Events.SchemaChange.Old.TaskCreated, Marten.Testing",

                Body = new {TaskId = taskId, Description = "initial description"}
            };

            var oldTaskDescriptionUpdatedEvent = new
            {
                Id = Guid.NewGuid(),
                StreamId = taskId,
                EventTypeName = "task_description_updated",

                // simulate namespace change from `Old` to `New`
                // and type name change from `TaskDescriptionUpdated` to `TaskDescriptionUpdatedNewName`
                DotnetTypeName = "Marten.Testing.Events.SchemaChange.Old.TaskDescriptionUpdated, Marten.Testing",
                Body = new {TaskId = taskId, Description = "updated description"}
            };

            using (var session = (DocumentSessionBase)theStore.OpenSession())
            {
                // ensure events tables already exists
                session.Tenant.EnsureStorageExists(typeof(StreamAction));

                // we need to insert data manually, as if we keep old type in assembly then Marten would use it.
                // Marten at first tries to find concrete type based on the `mt_dotnet_type` column value.
                // When Marten cannot find concrete CLR type then it searches it by `type` column value
                AppendRawEvent(session, oldTaskCreatedEvent);
                AppendRawEvent(session, oldTaskDescriptionUpdatedEvent);

                await session.SaveChangesAsync();
            }

            using (var store = SeparateStore(_ =>
            {
                // Add new Event types, if type names won't change then the same type name will be generated
                // and we don't need additional config
                _.Events.AddEventTypes(new []{typeof(New.TaskCreated), typeof(New.TaskDescriptionUpdatedNewName)});

                // When type name has changed we need to define custom mapping
                _.Events.EventMappingFor<New.TaskDescriptionUpdatedNewName>()
                        .EventTypeName = oldTaskDescriptionUpdatedEvent.EventTypeName;
            }))
            {
                using (var session = store.OpenSession())
                {
                    var taskNew = await session.Events.AggregateStreamAsync<New.Task>(taskId);

                    taskNew.Id.ShouldBe(taskId);
                    taskNew.Description.ShouldBe(oldTaskDescriptionUpdatedEvent.Body.Description);
                }
            }

            static void AppendRawEvent(DocumentSessionBase session, dynamic @event)
            {
                using var conn = session.Tenant.OpenConnection();

                conn.Execute(
                    $@"select * from {session.Options.Events.DatabaseSchemaName}.mt_append_event(
                                stream := '{@event.StreamId}',
                                stream_type := null,
                                tenantid := '{Tenancy.DefaultTenantId}',
                                event_ids := '{{""{@event.Id}""}}',
                                event_types := '{{""{@event.EventTypeName}""}}',
                                dotnet_types := '{{""{@event.DotnetTypeName}""}}',
                                bodies := '{{""{JsonConvert.SerializeObject(@event.Body).Replace("\"", "\\\"")}""}}')");
            }
        }

        public EventsNamespaceChange() : base("events_namespace_migration")
        {
        }
    }
}
