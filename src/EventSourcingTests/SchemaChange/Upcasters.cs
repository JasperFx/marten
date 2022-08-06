#nullable enable
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Services.Json;
using Marten.Services.Json.Transformations.SystemTextJson;
using Marten.Testing;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;
using static Marten.Services.Json.Transformations.SystemTextJson.Transformations;

namespace EventSourcingTests.SchemaChange
{
    #region sample_upcasters_old_event_type

    public record ShoppingCartOpened(
        Guid ShoppingCartId,
        Guid ClientId
    );

    #endregion


    #region sample_upcasters_new_event_type

    public record ShoppingCartInitializedWithStatus(
        Guid ShoppingCartId,
        Client Client,
        ShoppingCartStatus Status
    );

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

    #endregion

    public class ShoppingCartOpenedUpcasterWithClrTypes:
        Upcaster<ShoppingCartOpened, ShoppingCartInitializedWithStatus>
    {
        protected override ShoppingCartInitializedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
            new ShoppingCartInitializedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId),
                ShoppingCartStatus.Opened
            );
    }

    public class ShoppingCartOpenedUpcasterWithClrTypesAndExplicitEventTypeName:
        Upcaster<ShoppingCartOpened, ShoppingCartInitializedWithStatus>
    {
        public override string EventTypeName => "shopping_cart_opened";
        
        protected override ShoppingCartInitializedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
            new ShoppingCartInitializedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId),
                ShoppingCartStatus.Opened
            );
    }

    public class ShoppingCartOpenedUpcasterWithSystemTextJsonDocument:
        Upcaster<ShoppingCartInitializedWithStatus>
    {
        public override string EventTypeName => "shopping_cart_opened";

        protected override ShoppingCartInitializedWithStatus Upcast(JsonDocument oldEventJson)
        {
            var oldEvent = oldEventJson.RootElement;

            return new ShoppingCartInitializedWithStatus(
                oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                new Client(
                    oldEvent.GetProperty("ClientId").GetGuid()
                ),
                ShoppingCartStatus.Opened
            );
        }
    }

    public static class SampleEventsUpcasting
    {
        public static void LambdaWithClrTypes(StoreOptions options)
        {
            #region sample_upcast_lambda_event_with_systemtextjson_with_clr_types

            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.EventGraph
                .MapEventType<ShoppingCartInitializedWithStatus>(
                    "shopping_cart_opened",
                    Upcast((ShoppingCartOpened oldEvent) =>
                        new ShoppingCartInitializedWithStatus(
                            oldEvent.ShoppingCartId,
                            new Client(oldEvent.ClientId),
                            ShoppingCartStatus.Opened
                        )
                    )
                );

            #endregion
        }

        public static void LambdaWithSystemTextJsonJsonDocument(StoreOptions options)
        {
            #region sample_upcast_lambda_event_with_systemtextjson_json_document

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

            #endregion
        }

        public static void ClassWithClrTypes(StoreOptions options)
        {
            #region sample_upcast_class_event_with_systemtextjson_with_clr_types

            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.EventGraph.Upcast<ShoppingCartOpenedUpcasterWithClrTypes>();

            #endregion
        }

        public static void ClassWithSystemTextJsonJsonDocument(StoreOptions options)
        {
            #region sample_upcast_class_event_with_systemtextjson_json_document

            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.EventGraph.Upcast<ShoppingCartOpenedUpcasterWithSystemTextJsonDocument>();

            #endregion
        }
    }

    // Events in old namespace `Old`
    namespace Old
    {
        public class ShoppingCart: AggregateBase
        {
            public Guid ClientId { get; private set; }

            private ShoppingCart() { }

            public ShoppingCart(Guid id, Guid clientId)
            {
                var @event = new ShoppingCartOpened(id, clientId);
                EnqueueEvent(@event);
                Apply(@event);
            }

            private void Apply(ShoppingCartOpened @event)
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

    public class UpcastersTests: OneOffConfigurationsContext
    {
        [Theory]
        [MemberData(nameof(UpcastersConfiguration))]
        public async Task HavingEvents_WithSchemaChange_AggregationShouldWork(Action<StoreOptions> configureUpcasters)
        {
            // test events data
            var taskId = Guid.NewGuid();
            var clientId = Guid.NewGuid();

            var task = new Old.ShoppingCart(taskId, clientId);

            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await using (var session = (DocumentSessionBase)theStore.OpenSession())
            {
                session.Events.Append(taskId, (IEnumerable<object>)task.DequeueEvents());
                await session.SaveChangesAsync();
            }

            using var store = SeparateStore(configureUpcasters);
            {
                await using var session = store.OpenSession();
                var taskNew = await session.Events.AggregateStreamAsync<New.ShoppingCart>(taskId);

                taskNew.Id.ShouldBe(taskId);
                taskNew.Client.ShouldNotBeNull();
                taskNew.Client.Id.ShouldBe(task.ClientId);
            }
        }

        public static IEnumerable<Action<StoreOptions>> UpcastersConfiguration =>
            new List<Action<StoreOptions>>
            {
                SampleEventsUpcasting.LambdaWithClrTypes,
                SampleEventsUpcasting.LambdaWithSystemTextJsonJsonDocument,
                SampleEventsUpcasting.ClassWithClrTypes,
                SampleEventsUpcasting.ClassWithSystemTextJsonJsonDocument
            };
    }
}
#endif
