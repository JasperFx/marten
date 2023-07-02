#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
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

    public record ShoppingCartOpenedWithStatus(
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


    #region sample_async_upcaster_dependency

    public interface IClientRepository
    {
        Task<string> GetClientName(Guid clientId, CancellationToken ct);
    }

    #endregion

    public class DummyClientRepository: IClientRepository
    {
        private readonly Func<Guid, string> _getClientName;

        public DummyClientRepository(Func<Guid, string> getClientName) =>
            _getClientName = getClientName;

        public Task<string> GetClientName(Guid clientId, CancellationToken ct) =>
            Task.FromResult(_getClientName(clientId));
    }

    namespace ClrTypes
    {
        #region sample_upcaster_with_clr_types_and_event_type_name_from_old_type

        public class ShoppingCartOpenedUpcaster:
            EventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
        {
            protected override ShoppingCartOpenedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
                new ShoppingCartOpenedWithStatus(
                    oldEvent.ShoppingCartId,
                    new Client(oldEvent.ClientId),
                    ShoppingCartStatus.Opened
                );
        }

        #endregion

        #region sample_async_only_upcaster_with_clr_types_and_event_type_name_from_old_type

        public class ShoppingCartOpenedAsyncOnlyUpcaster:
            AsyncOnlyEventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
        {
            private readonly IClientRepository _clientRepository;

            public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
                _clientRepository = clientRepository;

            protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
                ShoppingCartOpened oldEvent,
                CancellationToken ct
            )
            {
                // WARNING: UpcastAsync method is called each time old event
                // is read from database and deserialized.
                // We discourage to run resource consuming methods here.
                // It might end up with N+1 problem.
                var clientName = await _clientRepository.GetClientName(oldEvent.ClientId, ct);

                return new ShoppingCartOpenedWithStatus(
                    oldEvent.ShoppingCartId,
                    new Client(oldEvent.ClientId, clientName),
                    ShoppingCartStatus.Opened
                );
            }
        }

        #endregion

        namespace ExplicitTypeName
        {
            #region sample_upcaster_with_clr_types_and_explicit_event_type_name

            public class ShoppingCartOpenedUpcaster:
                EventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
            {
                // Explicit event type name mapping may be useful if you used other than default event type name
                // for old event type.
                public override string EventTypeName => "shopping_cart_opened";

                protected override ShoppingCartOpenedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
                    new ShoppingCartOpenedWithStatus(
                        oldEvent.ShoppingCartId,
                        new Client(oldEvent.ClientId),
                        ShoppingCartStatus.Opened
                    );
            }

            #endregion

            #region sample_async_only_upcaster_with_clr_types_and_explicit_event_type_name

            public class ShoppingCartOpenedAsyncOnlyUpcaster:
                AsyncOnlyEventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
            {
                // Explicit event type name mapping may be useful if you used other than default event type name
                // for old event type.
                public override string EventTypeName => "shopping_cart_opened";

                private readonly IClientRepository _clientRepository;

                public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
                    _clientRepository = clientRepository;

                protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
                    ShoppingCartOpened oldEvent,
                    CancellationToken ct
                )
                {
                    // WARNING: UpcastAsync method is called each time old event
                    // is read from database and deserialized.
                    // We discourage to run resource consuming methods here.
                    // It might end up with N+1 problem.
                    var clientName = await _clientRepository.GetClientName(oldEvent.ClientId, ct);

                    return new ShoppingCartOpenedWithStatus(
                        oldEvent.ShoppingCartId,
                        new Client(oldEvent.ClientId, clientName),
                        ShoppingCartStatus.Opened
                    );
                }
            }

            #endregion
        }

        public static class SampleEventsUpcasting
        {
            public static void LambdaWithClrTypes(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_clr_types

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
                        oldEvent =>
                            new ShoppingCartOpenedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId),
                                ShoppingCartStatus.Opened
                            )
                    );

                #endregion
            }

            public static void AsyncLambdaWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_lambda_with_clr_types

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
                        async (oldEvent, ct) =>
                        {
                            // WARNING: UpcastAsync method is called each time old event
                            // is read from database and deserialized.
                            // We discourage to run resource consuming methods here.
                            // It might end up with N+1 problem.
                            var clientName = await clientRepository.GetClientName(oldEvent.ClientId, ct);

                            return new ShoppingCartOpenedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId, clientName),
                                ShoppingCartStatus.Opened
                            );
                        }
                    );

                #endregion
            }

            public static void LambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_clr_types_and_explicit_type_name

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        oldEvent =>
                            new ShoppingCartOpenedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId),
                                ShoppingCartStatus.Opened
                            )
                    );

                #endregion
            }

            public static void AsyncLambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_lambda_with_clr_types_and_explicit_type_name

                options.Events
                    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        async (oldEvent, ct) =>
                        {
                            // WARNING: UpcastAsync method is called each time old event
                            // is read from database and deserialized.
                            // We discourage to run resource consuming methods here.
                            // It might end up with N+1 problem.
                            var clientName = await clientRepository.GetClientName(oldEvent.ClientId, ct);

                            return new ShoppingCartOpenedWithStatus(
                                oldEvent.ShoppingCartId,
                                new Client(oldEvent.ClientId, clientName),
                                ShoppingCartStatus.Opened
                            );
                        }
                    );

                #endregion
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                #region sample_upcast_event_class_with_clr_types

                options.Events.Upcast<ShoppingCartOpenedUpcaster>();

                #endregion
            }

            public static void AsyncClassWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_class_with_clr_types

                options.Events.Upcast(new ShoppingCartOpenedAsyncOnlyUpcaster(clientRepository));

                #endregion
            }

            public static void ClassWithClrTypesWithExplicitEventTypeName(StoreOptions options)
            {
                #region sample_upcast_event_class_with_clr_types_with_explicit_event_type_name

                options.Events.Upcast<ExplicitTypeName.ShoppingCartOpenedUpcaster>();

                #endregion
            }

            public static void AsyncClassWithClrTypesWithExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_class_with_clr_types_with_explicit_event_type_name

                options.Events.Upcast(new ExplicitTypeName.ShoppingCartOpenedAsyncOnlyUpcaster(clientRepository));

                #endregion
            }
        }
    }

    namespace SystemTextJson
    {
        #region sample_upcast_system_text_json_class_using

        using Marten.Services.Json.Transformations.SystemTextJson;

        #endregion

        #region sample_upcast_system_text_json_static_using

        using static Marten.Services.Json.Transformations.SystemTextJson.JsonTransformations;

        #endregion

        #region sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_systemtextjson_json_document

        public class ShoppingCartOpenedUpcaster:
            EventUpcaster<ShoppingCartOpenedWithStatus>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override ShoppingCartOpenedWithStatus Upcast(JsonDocument oldEventJson)
            {
                var oldEvent = oldEventJson.RootElement;

                return new ShoppingCartOpenedWithStatus(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new Client(
                        oldEvent.GetProperty("ClientId").GetGuid()
                    ),
                    ShoppingCartStatus.Opened
                );
            }
        }

        #endregion

        #region sample_async_upcaster_with_systemtextjson_json_document

        public class ShoppingCartOpenedAsyncOnlyUpcaster:
            AsyncOnlyEventUpcaster<ShoppingCartOpenedWithStatus>
        {
            private readonly IClientRepository _clientRepository;

            public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
                _clientRepository = clientRepository;

            public override string EventTypeName => "shopping_cart_opened";

            protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
                JsonDocument oldEventJson, CancellationToken ct
            )
            {
                var oldEvent = oldEventJson.RootElement;

                var clientId = oldEvent.GetProperty("ClientId").GetGuid();

                // WARNING: UpcastAsync method is called each time old event
                // is read from database and deserialized.
                // We discourage to run resource consuming methods here.
                // It might end up with N+1 problem.
                var clientName = await _clientRepository.GetClientName(clientId, ct);

                return new ShoppingCartOpenedWithStatus(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new Client(clientId, clientName),
                    ShoppingCartStatus.Opened
                );
            }
        }

        #endregion

        public static class SampleEventsUpcasting
        {
            public static void LambdaWithJsonDocument(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_systemtextjson_json_document

                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

                options.Events
                    .Upcast<ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        Upcast(oldEventJson =>
                        {
                            var oldEvent = oldEventJson.RootElement;

                            return new ShoppingCartOpenedWithStatus(
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

            public static void AsyncLambdaWithJsonDocument(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_lambda_with_systemtextjson_json_document

                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

                options.Events
                    .Upcast<ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        AsyncOnlyUpcast(async (oldEventJson, ct) =>
                        {
                            var oldEvent = oldEventJson.RootElement;

                            var clientId = oldEvent.GetProperty("ClientId").GetGuid();

                            // WARNING: UpcastAsync method is called each time
                            // old event is read from database and deserialized.
                            // We discourage to run resource consuming methods here.
                            // It might end up with N+1 problem.
                            var clientName = await clientRepository.GetClientName(clientId, ct);

                            return new ShoppingCartOpenedWithStatus(
                                oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                                new Client(clientId, clientName),
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

                options.Events.Upcast<ShoppingCartOpenedUpcaster>();

                #endregion
            }

            public static void AsyncClassWithJsonDocument(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_upcast_event_class_with_systemtextjson_json_document

                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

                options.Events.Upcast(new ShoppingCartOpenedAsyncOnlyUpcaster(clientRepository));

                #endregion
            }

            public static void LambdaWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypes(options);
            }

            public static void AsyncLambdaWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.AsyncLambdaWithClrTypes(options, clientRepository);
            }

            public static void LambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitEventTypeName(options);
            }

            public static void AsyncLambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.AsyncLambdaWithClrTypesAndExplicitEventTypeName(options,
                    clientRepository);
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypes(options);
            }

            public static void AsyncClassWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.AsyncClassWithClrTypes(options, clientRepository);
            }

            public static void ClassWithClrTypesWithExplicitEventTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypesWithExplicitEventTypeName(options);
            }

            public static void AsyncClassWithClrTypesWithExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
                ClrTypes.SampleEventsUpcasting.AsyncClassWithClrTypesWithExplicitEventTypeName(options,
                    clientRepository);
            }
        }
    }

    namespace JsonNet
    {
        #region sample_upcast_json_net_class_using

        using Marten.Services.Json.Transformations.JsonNet;

        #endregion

        #region sample_upcast_json_net_static_using

        using static Marten.Services.Json.Transformations.JsonNet.JsonTransformations;

        #endregion

        #region sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_jsonnet_jobject

        public class ShoppingCartOpenedUpcaster:
            EventUpcaster<ShoppingCartOpenedWithStatus>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override ShoppingCartOpenedWithStatus Upcast(JObject oldEvent) =>
                new ShoppingCartOpenedWithStatus(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new Client(
                        (Guid)oldEvent["ClientId"]!
                    ),
                    ShoppingCartStatus.Opened
                );
        }

        #endregion

        #region sample_async_upcaster_with_jsonnet_jobject
        public class ShoppingCartOpenedAsyncOnlyUpcaster:
            AsyncOnlyEventUpcaster<ShoppingCartOpenedWithStatus>
        {
            private readonly IClientRepository _clientRepository;

            public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
                _clientRepository = clientRepository;

            public override string EventTypeName => "shopping_cart_opened";

            protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
                JObject oldEvent,
                CancellationToken ct
            )
            {
                var clientId = (Guid)oldEvent["ClientId"]!;
                // WARNING: UpcastAsync method is called each time old event
                // is read from database and deserialized.
                // We discourage to run resource consuming methods here.
                // It might end up with N+1 problem.
                var clientName = await _clientRepository.GetClientName(clientId, ct);

                return new ShoppingCartOpenedWithStatus(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new Client(clientId, clientName),
                    ShoppingCartStatus.Opened
                );
            }
        }
        #endregion

        public static class SampleEventsUpcasting
        {
            public static void LambdaWithJObject(StoreOptions options)
            {
                #region sample_upcast_event_lambda_with_jsonnet_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events
                    .Upcast<ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        Upcast(oldEvent =>
                            new ShoppingCartOpenedWithStatus(
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

            public static void AsyncLambdaWithJObject(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_lambda_with_jsonnet_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events
                    .Upcast<ShoppingCartOpenedWithStatus>(
                        "shopping_cart_opened",
                        AsyncOnlyUpcast(async (oldEvent, ct) =>
                            {
                                var clientId = (Guid)oldEvent["ClientId"]!;
                                // WARNING: UpcastAsync method is called each time old event
                                // is read from database and deserialized.
                                // We discourage to run resource consuming methods here.
                                // It might end up with N+1 problem.
                                var clientName = await clientRepository.GetClientName(clientId, ct);

                                return new ShoppingCartOpenedWithStatus(
                                    (Guid)oldEvent["ShoppingCartId"]!,
                                    new Client(clientId, clientName),
                                    ShoppingCartStatus.Opened
                                );
                            }
                        )
                    );

                #endregion
            }

            public static void ClassWithJObject(StoreOptions options)
            {
                #region sample_upcast_event_class_with_jsonnet_json_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events.Upcast<ShoppingCartOpenedUpcaster>();

                #endregion
            }

            public static void AsyncClassWithJObject(StoreOptions options, IClientRepository clientRepository)
            {
                #region sample_async_upcast_event_class_with_jsonnet_json_jobject

                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

                options.Events.Upcast(new ShoppingCartOpenedAsyncOnlyUpcaster(clientRepository));

                #endregion
            }

            public static void LambdaWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypes(options);
            }

            public static void AsyncLambdaWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.AsyncLambdaWithClrTypes(options, clientRepository);
            }

            public static void LambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitEventTypeName(options);
            }

            public static void AsyncLambdaWithClrTypesAndExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.AsyncLambdaWithClrTypesAndExplicitEventTypeName(options,
                    clientRepository);
            }

            public static void ClassWithClrTypes(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypes(options);
            }

            public static void AsyncClassWithClrTypes(StoreOptions options, IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.AsyncClassWithClrTypes(options, clientRepository);
            }

            public static void ClassWithClrTypesWithExplicitEventTypeName(StoreOptions options)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.ClassWithClrTypes(options);
            }

            public static void AsyncClassWithClrTypesWithExplicitEventTypeName(StoreOptions options,
                IClientRepository clientRepository)
            {
                options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
                ClrTypes.SampleEventsUpcasting.AsyncClassWithClrTypesWithExplicitEventTypeName(options,
                    clientRepository);
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
        public class ShoppingCartAggregate: AggregateBase
        {
            public Client Client { get; private set; }
            public ShoppingCartStatus Status { get; private set; }

            private ShoppingCartAggregate() { }

            public ShoppingCartAggregate(Guid id, Client client, ShoppingCartStatus status)
            {
                var @event = new ShoppingCartOpenedWithStatus(id, client, status);
                EnqueueEvent(@event);
                Apply(@event);
            }

            public void Apply(ShoppingCartOpenedWithStatus @event)
            {
                Id = @event.ShoppingCartId;
                Client = @event.Client;
                Status = @event.Status;
            }
        }

        public sealed class ShoppingCart
        {
            public Guid Id { get; set; }
            public Client Client { get; set; }
            public ShoppingCartStatus Status { get; set; }

            public void Apply(ShoppingCartOpenedWithStatus @event)
            {
                Id = @event.ShoppingCartId;
                Client = @event.Client;
                Status = @event.Status;
            }
        }

        public class ShoppingCartProjection: SingleStreamProjection<ShoppingCart>
        {

        }
    }

    public class UpcastersTests: OneOffConfigurationsContext
    {
        [Theory]
        [MemberData(nameof(UpcastersConfiguration))]
        public async Task HavingEvents_WithSchemaChange_AggregationShouldWork(Action<StoreOptions> configureUpcasters)
        {
            // test events data
            var shoppingCartId = Guid.NewGuid();
            var clientId = Guid.NewGuid();

            var shoppingCart = new Old.ShoppingCart(shoppingCartId, clientId);

            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(shoppingCartId, (IEnumerable<object>)shoppingCart.DequeueEvents());
                await session.SaveChangesAsync();
            }

            using var store = SeparateStore(_ =>
            {
                _.Projections.Add<New.ShoppingCartProjection>(ProjectionLifecycle.Inline);

                configureUpcasters(_);
            });
            {
                await using var session = store.LightweightSession();
                var shoppingCartNew = await session.Events.AggregateStreamAsync<New.ShoppingCartAggregate>(shoppingCartId);

                shoppingCartNew!.Id.ShouldBe(shoppingCartId);
                shoppingCartNew.Client.ShouldNotBeNull();
                shoppingCartNew.Client.Id.ShouldBe(shoppingCart.ClientId);


                using var daemon = await store.BuildProjectionDaemonAsync();

                await daemon.RebuildProjection<New.ShoppingCartProjection>(CancellationToken.None);

                var shoppingCartRebuilt = await session.LoadAsync<New.ShoppingCart>(shoppingCartId);

                shoppingCartRebuilt!.Id.ShouldBe(shoppingCartId);
                shoppingCartRebuilt.Client.ShouldNotBeNull();
                shoppingCartRebuilt.Client.Id.ShouldBe(shoppingCart.ClientId);

            }
        }

        private const string ClientName = "DummyClientName";
        public static readonly IClientRepository clientRepository = new DummyClientRepository(_ => ClientName);

        public static TheoryData<Action<StoreOptions>> UpcastersConfiguration =>
            new()
            {
                JsonNet.SampleEventsUpcasting.LambdaWithClrTypes,
                JsonNet.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitEventTypeName,
                JsonNet.SampleEventsUpcasting.LambdaWithJObject,
                JsonNet.SampleEventsUpcasting.ClassWithClrTypes,
                JsonNet.SampleEventsUpcasting.ClassWithJObject,
                options => JsonNet.SampleEventsUpcasting.AsyncLambdaWithClrTypes(options, clientRepository),
                options => JsonNet.SampleEventsUpcasting.AsyncLambdaWithClrTypesAndExplicitEventTypeName(options,
                    clientRepository),
                options => JsonNet.SampleEventsUpcasting.AsyncLambdaWithJObject(options, clientRepository),
                options => JsonNet.SampleEventsUpcasting.AsyncClassWithClrTypes(options, clientRepository),
                options => JsonNet.SampleEventsUpcasting.AsyncClassWithClrTypesWithExplicitEventTypeName(options,
                    clientRepository),
                options => JsonNet.SampleEventsUpcasting.AsyncClassWithJObject(options, clientRepository),
                SystemTextJson.SampleEventsUpcasting.LambdaWithClrTypes,
                SystemTextJson.SampleEventsUpcasting.LambdaWithClrTypesAndExplicitEventTypeName,
                SystemTextJson.SampleEventsUpcasting.LambdaWithJsonDocument,
                SystemTextJson.SampleEventsUpcasting.ClassWithClrTypes,
                SystemTextJson.SampleEventsUpcasting.ClassWithJsonDocument,
                options => SystemTextJson.SampleEventsUpcasting.AsyncLambdaWithClrTypes(options, clientRepository),
                options =>
                    SystemTextJson.SampleEventsUpcasting.AsyncLambdaWithClrTypesAndExplicitEventTypeName(options,
                        clientRepository),
                options => SystemTextJson.SampleEventsUpcasting.AsyncLambdaWithJsonDocument(options, clientRepository),
                options => SystemTextJson.SampleEventsUpcasting.AsyncClassWithClrTypes(options, clientRepository),
                options =>
                    SystemTextJson.SampleEventsUpcasting.AsyncClassWithClrTypesWithExplicitEventTypeName(options,
                        clientRepository),
                options => SystemTextJson.SampleEventsUpcasting.AsyncClassWithJsonDocument(options, clientRepository),
            };
    }
}
