#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Services.Json;
using Marten.Testing;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;
using static Marten.Services.Json.SystemTextJson.Transformations;

namespace EventSourcingTests.SchemaChange.Upcasters
{
    #region sample_upcasters_old_event_type

    namespace OldEventNamespace
    {
        public record ShoppingCartOpened(
            Guid ShoppingCartId,
            Guid ClientId
        );
    }

    #endregion

    public record Client(
        Guid Id,
        string Name = "Unknown"
    );

    public enum ShoppingCartStatus
    {
        Pending = 1,
        Opened = 2,
        Confirmed = 3,
        Cancelled = 4
    }

    public record ShoppingCartInitializedWithStatus(
        Guid ShoppingCartId,
        Client Client,
        ShoppingCartStatus Status
    );

    public static class SampleEventsUpcasting
    {
        public static void WithClrTypes()
        {
            #region sample_upcast_event_with_systemtextjson_with_clr_types

            var options = new StoreOptions();
            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.EventGraph
                .MapEventType<ShoppingCartInitializedWithStatus>("shopping_cart_opened",
                    Upcast((OldEventNamespace.ShoppingCartOpened oldEvent) =>
                        new ShoppingCartInitializedWithStatus(
                            oldEvent.ShoppingCartId,
                            new Client(oldEvent.ClientId),
                            ShoppingCartStatus.Opened
                        )
                    )
                );

            #endregion
        }

        public static void WithSystemTextJsonJsonDocument()
        {
            #region sample_upcast_event_with_systemtextjson_json_document

            var options = new StoreOptions();
            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.EventGraph
                .MapEventType<ShoppingCartInitializedWithStatus>(
                    "shopping_cart_opened",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new ShoppingCartInitializedWithStatus(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new Client(
                                oldEvent.GetProperty("ClientId").GetGuid()
                            ),
                            ShoppingCartStatus.Opened
                        );
                    })
                );

            var store = new DocumentStore(options);

            #endregion
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
    namespace OldEventNamespace
    {
        public class ShoppingCart: AggregateBase
        {
            public Guid ClientId { get; private set; }

            public ShoppingCart(Guid id, Guid clientId)
            {
                var @event = new ShoppingCartOpened(id, clientId);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void Apply(ShoppingCartOpened @event)
            {
                Id = @event.ShoppingCartId;
                ClientId = @event.ClientId;
            }
        }
    }

    // Events in new namespace `New`
    namespace New
    {
        public class ShoppingCart: AggregateBase
        {
            public Client Client { get; private set; }
            public ShoppingCartStatus Status { get; private set; }

            private ShoppingCart() { }

            public ShoppingCart(Guid id, Client client, ShoppingCartStatus status)
            {
                var @event = new ShoppingCartInitializedWithStatus(id, client, status);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void Apply(ShoppingCartInitializedWithStatus @event)
            {
                Id = @event.ShoppingCartId;
                Client = @event.Client;
                Status = @event.Status;
            }
        }
    }

    public class EventsNamespaceChange: OneOffConfigurationsContext
    {
        [Fact]
        public async Task HavingEvents_WithSchemaChange_AggregationShouldWork()
        {
            // test events data
            var taskId = Guid.NewGuid();
            var clientId = Guid.NewGuid();

            var task = new OldEventNamespace.ShoppingCart(taskId, clientId);

            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await using (var session = (DocumentSessionBase)theStore.OpenSession())
            {
                session.Events.Append(taskId, task.DequeueEvents());
                await session.SaveChangesAsync();
            }

            using (var store = SeparateStore(_ =>
                   {
                       // When type name has changed we need to define custom mapping
                       _.Events.MapEventType<ShoppingCartInitializedWithStatus>(
                           "task_description_updated",
                           Upcast((OldEventNamespace.ShoppingCartOpened oldEvent) =>
                               new ShoppingCartInitializedWithStatus(
                                   oldEvent.ShoppingCartId,
                                   new Client(oldEvent.ClientId),
                                   ShoppingCartStatus.Opened
                               )
                           )
                       );
                   }))
            {
                await using (var session = store.OpenSession())
                {
                    var taskNew = await session.Events.AggregateStreamAsync<New.ShoppingCart>(taskId);

                    taskNew.Id.ShouldBe(taskId);
                    taskNew.Client.ShouldNotBeNull();
                    taskNew.Client.Id.ShouldBe(task.ClientId);
                }
            }
        }
    }
}
#endif
