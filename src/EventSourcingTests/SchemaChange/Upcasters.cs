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
using Marten.Services.Json.Transformations;
using Marten.Testing;
using Marten.Testing.Harness;
using Newtonsoft.Json.Linq;
using Xunit;
using Shouldly;

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

    namespace ClrTypes
    {
        using static Transformations;

        #region sample_upcaster_with_clr_types_and_event_type_name_from_old_type

        public class ShoppingCartOpenedEventUpcasterWithClrTypes:
            EventUpcaster<ShoppingCartOpened, ShoppingCartInitializedWithStatus>
        {
            protected override ShoppingCartInitializedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
                new ShoppingCartInitializedWithStatus(
                    oldEvent.ShoppingCartId,
                    new Client(oldEvent.ClientId),
                    ShoppingCartStatus.Opened
                );
        }

        #endregion


        #region sample_upcaster_with_clr_types_and_explicit_event_type_name

        public class ShoppingCartOpenedEventUpcasterWithClrTypesAndExplicitTypeName:
            EventUpcaster<ShoppingCartOpened, ShoppingCartInitializedWithStatus>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override ShoppingCartInitializedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
                new ShoppingCartInitializedWithStatus(
                    oldEvent.ShoppingCartId,
                    new Client(oldEvent.ClientId),
                    ShoppingCartStatus.Opened
                );
        }

        #endregion

        public static class SampleEventsUpcasting
        {
            public static void LambdaWithClrTypes(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_clr_types

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartInitializedWithStatus>(
                        oldEvent =>
                            new ShoppingCartInitializedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId),
                                ShoppingCartStatus.Opened
                            )
                    );

                #endregion
            }

            public static void LambdaWithClrTypesAndExplicitTypeName(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_clr_types_and_explicit_type_name

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartInitializedWithStatus>(
                        "shopping_cart_opened",
                        oldEvent =>
                            new ShoppingCartInitializedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId),
                                ShoppingCartStatus.Opened
                            )
                    );

                #endregion
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                #region sample_upcast_event_class_with_clr_types

                options.Events.Upcast<ShoppingCartOpenedEventUpcasterWithClrTypes>();

                #endregion
            }
        }
    }

    namespace SystemTextJson
    {
        using Marten.Services.Json.Transformations.SystemTextJson;
        using static Marten.Services.Json.Transformations.SystemTextJson.Transformations;

        public class ShoppingCartOpenedEventUpcasterWithSystemTextJsonDocument:
            EventUpcaster<ShoppingCartInitializedWithStatus>
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
            public static void LambdaWithJsonDocument(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_systemtextjson_json_document

                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

                options.Events
                    .Upcast<ShoppingCartInitializedWithStatus>(
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

            public static void ClassWithJsonDocument(StoreOptions options)
            {
                #region sample_upcast_event_class_with_systemtextjson_json_document

                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

                options.Events.Upcast<ShoppingCartOpenedEventUpcasterWithSystemTextJsonDocument>();

                #endregion
            }

            public static void LambdaWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypes(options);
            }

            public static void LambdaWithClrTypesAndExplicitTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitTypeName(options);
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypes(options);
            }
        }
    }


    namespace JsonNet
    {
        using Marten.Services.Json.Transformations.JsonNet;
        using static Marten.Services.Json.Transformations.JsonNet.Transformations;

        public class ShoppingCartOpenedEventUpcasterWithNewtonsoftJObject:
            EventUpcaster<ShoppingCartInitializedWithStatus>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override ShoppingCartInitializedWithStatus Upcast(JObject oldEvent) =>
                new ShoppingCartInitializedWithStatus(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new Client(
                        (Guid)oldEvent["ClientId"]!
                    ),
                    ShoppingCartStatus.Opened
                );
        }

        public static class SampleEventsUpcasting
        {
            public static void LambdaWithJObject(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_jsonnet_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events
                    .Upcast<ShoppingCartInitializedWithStatus>(
                        "shopping_cart_opened",
                        Upcast(oldEvent =>
                            new ShoppingCartInitializedWithStatus(
                                (Guid)oldEvent["ShoppingCartId"]!,
                                new Client(
                                    (Guid)oldEvent["ClientId"]!
                                ),
                                ShoppingCartStatus.Opened
                            )
                        )
                    );

                #endregion
            }

            public static void ClassWithJObject(StoreOptions options)
            {
                #region sample_upcast_event_class_with_jsonnet_json_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events.Upcast<ShoppingCartOpenedEventUpcasterWithNewtonsoftJObject>();

                #endregion
            }

            public static void LambdaWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypes(options);
            }

            public static void LambdaWithClrTypesAndExplicitTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitTypeName(options);
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypes(options);
            }
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

        public static TheoryData<Action<StoreOptions>> UpcastersConfiguration =>
            new()
            {
                JsonNet.SampleEventsUpcasting.LambdaWithClrTypes,
                JsonNet.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitTypeName,
                JsonNet.SampleEventsUpcasting.LambdaWithJObject,
                JsonNet.SampleEventsUpcasting.ClassWithClrTypes,
                JsonNet.SampleEventsUpcasting.ClassWithJObject,
                SystemTextJson.SampleEventsUpcasting.LambdaWithClrTypes,
                SystemTextJson.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitTypeName,
                SystemTextJson.SampleEventsUpcasting.LambdaWithJsonDocument,
                SystemTextJson.SampleEventsUpcasting.ClassWithClrTypes,
                SystemTextJson.SampleEventsUpcasting.ClassWithJsonDocument
            };
    }
}
#endif
