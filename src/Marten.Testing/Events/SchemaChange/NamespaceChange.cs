using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.SchemaChange
{
    #region sample_old_event_namespace
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
    #endregion sample_old_event_namespace

    #region sample_new_event_namespace
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
    #endregion sample_new_event_namespace


    #region sample_new_event_type_name
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
    #endregion sample_new_event_type_name

    public static class SampleEventsSchemaMigration
    {
        public static void SampleAddEventsRegistration()
        {
            #region sample_event_namespace_migration_options
            var options = new StoreOptions();

            options.Events.AddEventTypes(new[] {typeof(NewEventNamespace.OrderStatusChanged)});

            var store = new DocumentStore(options);
            #endregion sample_event_namespace_migration_options
        }

        public static void SampleEventMappingRegistration()
        {
            #region sample_event_type_name_migration_options
            var options = new StoreOptions();

            var orderStatusChangedMapping = options.EventGraph.EventMappingFor<OldEventNamespace.ConfirmedOrderStatusChanged>();
            orderStatusChangedMapping.EventTypeName = "order_status_changed";

            var store = new DocumentStore(options);
            #endregion sample_event_type_name_migration_options
        }
    }

    public abstract class AggregateBase
    {
        public Guid Id { get; protected set; }
        public long Version { get; protected set; }

        [JsonIgnore] private readonly List<object> _uncommittedEvents = new List<object>();

        public IEnumerable<object> DequeueEvents()
        {
            var events = _uncommittedEvents.ToList();
            _uncommittedEvents.Clear();
            return events;
        }

        protected void EnqueueEvent(object @event)
        {
            // add the event to the uncommitted list
            _uncommittedEvents.Add(@event);
            Version++;
        }
    }

    // Events in old namespace `Old`
    namespace Old
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

        public class TaskDescriptionUpdated
        {
            public string Description { get; }

            public TaskDescriptionUpdated(string description)
            {
                Description = description;
            }
        }

        public class Task : AggregateBase
        {
            public string Description { get; private set; }

            private Task() {}

            public Task(Guid id, string description)
            {
                var @event = new TaskCreated(id, description);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void UpdateDescription(string description)
            {
                var @event = new TaskDescriptionUpdated(description);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void Apply(TaskCreated @event)
            {
                Id = @event.TaskId;
                Description = @event.Description;
            }

            public void Apply(TaskDescriptionUpdated @event)
            {
                Description = @event.Description;
            }
        }
    }

    // Events in new namespace `New`
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

        // Type name has changed - Event will be stored with `TaskDescriptionChanged`
        public class TaskDescriptionChanged
        {
            public Guid TaskId { get; }
            public string Description { get; }

            public TaskDescriptionChanged(string description)
            {
                Description = description;
            }
        }


        public class Task : AggregateBase
        {
            public string Description { get; private set; }

            private Task() {}

            public Task(Guid id, string description)
            {
                var @event = new TaskCreated(id, description);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void UpdateDescription(string description)
            {
                var @event = new TaskDescriptionChanged(description);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void Apply(TaskCreated @event)
            {
                Id = @event.TaskId;
                Description = @event.Description;
            }

            public void Apply(TaskDescriptionChanged @event)
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

            var task = new Old.Task(taskId, "Initial Description");
            task.UpdateDescription("updated description");

            theStore.Tenancy.Default.EnsureStorageExists(typeof(StreamAction));

            using (var session = (DocumentSessionBase)theStore.OpenSession())
            {
                session.Events.Append(taskId, task.DequeueEvents());
                await session.SaveChangesAsync();
            }

            using (var store = SeparateStore(_ =>
            {
                // Add new Event types, if type names won't change then the same type name will be generated
                // and we don't need additional config
                _.Events.AddEventTypes(new []{typeof(New.TaskCreated), typeof(New.TaskDescriptionChanged)});

                // When type name has changed we need to define custom mapping
                _.EventGraph.EventMappingFor<New.TaskDescriptionChanged>()
                        .EventTypeName = "task_description_updated";
            }))
            {
                using (var session = store.OpenSession())
                {
                    var taskNew = await session.Events.AggregateStreamAsync<New.Task>(taskId);

                    taskNew.Id.ShouldBe(taskId);
                    taskNew.Description.ShouldBe(task.Description);
                }
            }
        }

        public EventsNamespaceChange() : base("events_namespace_migration")
        {
        }
    }
}
