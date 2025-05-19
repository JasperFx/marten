#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.SchemaChange.MultipleVersions;

public class MultipleSchemaVersions: OneOffConfigurationsContext
{
    [Theory]
    [MemberData(nameof(UpcastersConfigurationWithDifferentName))]
    public async Task UpcastingWithMultipleSchemaAndDifferentTypeNamesShouldWork(
        Action<StoreOptions> configureV2Upcasters,
        Action<StoreOptions> configureV3Upcasters
    )
    {
        // test events data
        var shoppingCartId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var currentQuantity = 3;
        var price = 1.23m;

        StoreOptions(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V1.ShoppingCart>(SnapshotLifecycle.Inline);
        });
        await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

        await AppendEventsInV1Schema(
            shoppingCartId,
            clientId,
            productId,
            currentQuantity
        );

        currentQuantity = await CheckV2WithDifferentNameUpcasting(
            configureV2Upcasters,
            shoppingCartId,
            clientId,
            productId,
            currentQuantity,
            price
        );

        await CheckV3WithDifferentNameUpcasting(
            configureV3Upcasters,
            shoppingCartId,
            clientId,
            productId,
            currentQuantity,
            price
        );
    }

    [Theory]
    [MemberData(nameof(UpcastersConfigurationWithTheSameName))]
    public async Task UpcastingWithMultipleSchemaAndTheSameTypeNamesShouldWork(
        Action<StoreOptions> configureV2Upcasters,
        Action<StoreOptions> configureV3Upcasters
    )
    {
        // test events data
        var shoppingCartId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var currentQuantity = 3;
        var price = 1.23m;

        StoreOptions(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V1.ShoppingCart>(SnapshotLifecycle.Inline);
        });
        await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

        await AppendEventsInV1Schema(
            shoppingCartId,
            clientId,
            productId,
            currentQuantity
        );

        currentQuantity = await CheckV2WithTheSameNameUpcasting(
            configureV2Upcasters,
            shoppingCartId,
            clientId,
            productId,
            currentQuantity,
            price
        );

        await CheckV3WithTheSameNameUpcasting(
            configureV3Upcasters,
            shoppingCartId,
            clientId,
            productId,
            currentQuantity,
            price
        );
    }

    private async Task AppendEventsInV1Schema(Guid shoppingCartId, Guid clientId, Guid productId,
        int initialQuantity)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(shoppingCartId,
            new V1.ShoppingCartOpened(shoppingCartId, clientId));
        session.Events.Append(shoppingCartId,
            new V1.ProductItemAddedToShoppingCart(shoppingCartId, productId, initialQuantity));
        await session.SaveChangesAsync();
    }

    private async Task<int> CheckV2WithTheSameNameUpcasting(
        Action<StoreOptions> configureV2Upcasters,
        Guid shoppingCartId,
        Guid clientId,
        Guid productId,
        int currentQuantity,
        decimal price)
    {
        using var storeV2 = SeparateStore(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V2.WithTheSameName.ShoppingCart>(SnapshotLifecycle.Inline);
            ////////////////////////////////////////////////////////
            // 2.1. Define Upcast methods from V1 to V2
            ////////////////////////////////////////////////////////
            configureV2Upcasters(options);
        });
        {
            await using var session = storeV2.LightweightSession();

            ////////////////////////////////////////////////////////
            // 2.2. Append Event with V2 schema
            ////////////////////////////////////////////////////////
            var additionalQuantity = 2;
            session.Events.Append(shoppingCartId,
                new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                    shoppingCartId,
                    productId,
                    additionalQuantity,
                    price
                )
            );
            await session.SaveChangesAsync();

            ////////////////////////////////////////////////////////
            // 2.3. Ensure that all events are read with V2 schema
            ////////////////////////////////////////////////////////
            var events = await session.Events.FetchStreamAsync(shoppingCartId);
            events.Count.ShouldBe(3);
            events[0].ShouldBeOfType<V2.WithTheSameName.ShoppingCartOpened>(
                "shopping_cart_opened"
            );
            events[1].ShouldBeOfType<V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                "product_item_added_to_shopping_cart"
            );
            events[2].ShouldBeOfType<V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                "product_item_added_to_shopping_cart_v2"
            );

            ////////////////////////////////////////////////////////
            // 2.4. Ensure that aggregation is using V2 schema
            ////////////////////////////////////////////////////////
            var shoppingCartV2 =
                await session.Events.AggregateStreamAsync<V2.WithTheSameName.ShoppingCart>(
                    shoppingCartId
                );

            var shoppingCartV2Projection =
                await session.LoadAsync<V2.WithTheSameName.ShoppingCart>(shoppingCartId);

            shoppingCartV2
                .ShouldNotBeNull()
                .ShouldBeEquivalentTo(
                    new V2.WithTheSameName.ShoppingCart(
                        shoppingCartId,
                        clientId,
                        new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                        V2.ShoppingCartStatus.Opened,
                        null
                    )
                );

            shoppingCartV2Projection.ShouldBeEquivalentTo(shoppingCartV2);

            return currentQuantity + additionalQuantity;
        }
    }

    private async Task CheckV3WithTheSameNameUpcasting(
        Action<StoreOptions> configureV3Upcasters,
        Guid shoppingCartId,
        Guid clientId,
        Guid productId,
        int currentQuantity,
        decimal price
    )
    {
        using var storeV3 = SeparateStore(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V3.WithTheSameName.ShoppingCart>(SnapshotLifecycle.Inline);
            ////////////////////////////////////////////////////////
            // 3.1. Define Upcast methods from V1 to V3, and from V2 to V3
            ////////////////////////////////////////////////////////
            configureV3Upcasters(options);
        });
        {
            await using var session = storeV3.LightweightSession();

            ////////////////////////////////////////////////////////
            // 3.2. Append Event with V3 schema
            ////////////////////////////////////////////////////////
            var additionalQuantity = 4;
            session.Events.Append(shoppingCartId,
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    shoppingCartId,
                    new V3.ProductItem(
                        productId,
                        additionalQuantity,
                        price
                    )
                )
            );
            await session.SaveChangesAsync();

            ////////////////////////////////////////////////////////
            // 3.3. Ensure that all events are read with V2 schema
            ////////////////////////////////////////////////////////
            var events = await session.Events.FetchStreamAsync(shoppingCartId);
            events.Count.ShouldBe(4);
            events[0].ShouldBeOfType<V3.WithTheSameName.ShoppingCartOpened>(
                "shopping_cart_opened"
            );
            events[1].ShouldBeOfType<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                "product_item_added_to_shopping_cart"
            );
            events[2].ShouldBeOfType<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                "product_item_added_to_shopping_cart_v2"
            );
            events[3].ShouldBeOfType<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                "product_item_added_to_shopping_cart_v3"
            );

            ////////////////////////////////////////////////////////
            // 3.4. Ensure that aggregation is using V3 schema
            ////////////////////////////////////////////////////////
            var shoppingCartV3 =
                await session.Events.AggregateStreamAsync<V3.WithTheSameName.ShoppingCart>(
                    shoppingCartId
                );

            var shoppingCartV3Projection =
                await session.LoadAsync<V3.WithTheSameName.ShoppingCart>(shoppingCartId);

            shoppingCartV3
                .ShouldNotBeNull()
                .ShouldBeEquivalentTo(
                    new V3.WithTheSameName.ShoppingCart(
                        shoppingCartId,
                        clientId,
                        new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                        V3.ShoppingCartStatus.Opened,
                        null
                    )
                );

            shoppingCartV3Projection.ShouldBeEquivalentTo(shoppingCartV3);
        }
    }

    private async Task<int> CheckV2WithDifferentNameUpcasting(
        Action<StoreOptions> configureV2Upcasters,
        Guid shoppingCartId,
        Guid clientId,
        Guid productId,
        int currentQuantity,
        decimal price)
    {
        using var storeV2 = SeparateStore(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V2.WithDifferentName.ShoppingCart>(SnapshotLifecycle.Inline);
            ////////////////////////////////////////////////////////
            // 2.1. Define Upcast methods from V1 to V2
            ////////////////////////////////////////////////////////
            configureV2Upcasters(options);
        });
        {
            await using var session = storeV2.LightweightSession();

            ////////////////////////////////////////////////////////
            // 2.2. Append Event with V2 schema
            ////////////////////////////////////////////////////////
            var additionalQuantity = 2;
            session.Events.Append(shoppingCartId,
                new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                    shoppingCartId,
                    productId,
                    additionalQuantity,
                    price
                )
            );
            await session.SaveChangesAsync();

            ////////////////////////////////////////////////////////
            // 2.3. Ensure that all events are read with V2 schema
            ////////////////////////////////////////////////////////
            var events = await session.Events.FetchStreamAsync(shoppingCartId);
            events.Count.ShouldBe(3);
            events[0].ShouldBeOfType<V2.WithDifferentName.ShoppingCartOpenedV2>(
                "shopping_cart_opened"
            );
            events[1].ShouldBeOfType<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                "product_item_added_to_shopping_cart"
            );
            events[2].ShouldBeOfType<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                "product_item_added_to_shopping_cart_v2"
            );

            ////////////////////////////////////////////////////////
            // 2.4. Ensure that aggregation is using V2 schema
            ////////////////////////////////////////////////////////
            var shoppingCartV2 =
                await session.Events.AggregateStreamAsync<V2.WithDifferentName.ShoppingCart>(
                    shoppingCartId
                );

            var shoppingCartV2Projection =
                await session.LoadAsync<V2.WithDifferentName.ShoppingCart>(shoppingCartId);

            shoppingCartV2
                .ShouldNotBeNull()
                .ShouldBeEquivalentTo(
                    new V2.WithDifferentName.ShoppingCart(
                        shoppingCartId,
                        clientId,
                        new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                        V2.ShoppingCartStatus.Opened,
                        null
                    )
                );

            shoppingCartV2Projection.ShouldBeEquivalentTo(shoppingCartV2);

            return currentQuantity + additionalQuantity;
        }
    }

    private async Task CheckV3WithDifferentNameUpcasting(
        Action<StoreOptions> configureV3Upcasters,
        Guid shoppingCartId,
        Guid clientId,
        Guid productId,
        int currentQuantity,
        decimal price)
    {
        using var storeV3 = SeparateStore(options =>
        {
            options.GeneratedCodeMode = TypeLoadMode.Auto;
            options.Projections.Snapshot<V3.WithDifferentName.ShoppingCart>(SnapshotLifecycle.Inline);
            ////////////////////////////////////////////////////////
            // 3.1. Define Upcast methods from V1 to V3, and from V2 to V3
            ////////////////////////////////////////////////////////
            configureV3Upcasters(options);
        });
        {
            await using var session = storeV3.LightweightSession();

            ////////////////////////////////////////////////////////
            // 3.2. Append Event with V3 schema
            ////////////////////////////////////////////////////////
            var additionalQuantity = 4;
            session.Events.Append(shoppingCartId,
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    shoppingCartId,
                    new V3.ProductItem(
                        productId,
                        additionalQuantity,
                        price
                    )
                )
            );
            await session.SaveChangesAsync();

            ////////////////////////////////////////////////////////
            // 3.3. Ensure that all events are read with V2 schema
            ////////////////////////////////////////////////////////
            var events = await session.Events.FetchStreamAsync(shoppingCartId);
            events.Count.ShouldBe(4);
            events[0].ShouldBeOfType<V3.WithDifferentName.ShoppingCartOpenedV3>(
                "shopping_cart_opened"
            );
            events[1].ShouldBeOfType<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                "product_item_added_to_shopping_cart"
            );
            events[2].ShouldBeOfType<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                "product_item_added_to_shopping_cart_v2"
            );
            events[3].ShouldBeOfType<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                "product_item_added_to_shopping_cart_v3"
            );

            ////////////////////////////////////////////////////////
            // 3.4. Ensure that aggregation is using V3 schema
            ////////////////////////////////////////////////////////
            var shoppingCartV3 =
                await session.Events.AggregateStreamAsync<V3.WithDifferentName.ShoppingCart>(
                    shoppingCartId
                );

            var shoppingCartV3Projection =
                await session.LoadAsync<V3.WithDifferentName.ShoppingCart>(shoppingCartId);

            shoppingCartV3
                .ShouldNotBeNull()
                .ShouldBeEquivalentTo(
                    new V3.WithDifferentName.ShoppingCart(
                        shoppingCartId,
                        clientId,
                        new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                        V3.ShoppingCartStatus.Opened,
                        null
                    )
                );

            shoppingCartV3Projection.ShouldBeEquivalentTo(shoppingCartV3);
        }
    }

    public static TheoryData<Action<StoreOptions>, Action<StoreOptions>> UpcastersConfigurationWithTheSameName =>
        new()
        {
            { Lambdas.ClrUpcastConfiguration.V2WithTheSameName, Lambdas.ClrUpcastConfiguration.V3WithTheSameName },
            {
                Lambdas.SystemTextJsonUpcastConfiguration.V2WithTheSameName,
                Lambdas.SystemTextJsonUpcastConfiguration.V3WithTheSameName
            },
            {
                Lambdas.JsonNetUpcastConfiguration.V2WithTheSameName,
                Lambdas.JsonNetUpcastConfiguration.V3WithTheSameName
            },
            { Classes.ClrUpcastConfiguration.V2WithTheSameName, Classes.ClrUpcastConfiguration.V3WithTheSameName },
            {
                Classes.SystemTextJsonUpcastConfiguration.V2WithTheSameName,
                Classes.SystemTextJsonUpcastConfiguration.V3WithTheSameName
            },
            {
                Classes.JsonNetUpcastConfiguration.V2WithTheSameName,
                Classes.JsonNetUpcastConfiguration.V3WithTheSameName
            },
        };

    public static TheoryData<Action<StoreOptions>, Action<StoreOptions>> UpcastersConfigurationWithDifferentName =>
        new()
        {
            {
                Lambdas.ClrUpcastConfiguration.V2WithDifferentName, Lambdas.ClrUpcastConfiguration.V3WithDifferentName
            },
            {
                Lambdas.SystemTextJsonUpcastConfiguration.V2WithDifferentName,
                Lambdas.SystemTextJsonUpcastConfiguration.V3WithDifferentName
            },
            {
                Lambdas.JsonNetUpcastConfiguration.V2WithDifferentName,
                Lambdas.JsonNetUpcastConfiguration.V3WithDifferentName
            },
            { Classes.ClrUpcastConfiguration.V2WithDifferentName, Classes.ClrUpcastConfiguration.V3WithDifferentName },
            {
                Classes.SystemTextJsonUpcastConfiguration.V2WithDifferentName,
                Classes.SystemTextJsonUpcastConfiguration.V3WithDifferentName
            },
            {
                Classes.JsonNetUpcastConfiguration.V2WithDifferentName,
                Classes.JsonNetUpcastConfiguration.V3WithDifferentName
            }
        };
}

public static class UpcastingTestsExtensions
{
    public static void ShouldBeOfType<TEvent>(this IEvent @event, string eventTypeName)
    {
        @event.EventTypeName.ShouldBe(eventTypeName);
        @event.EventType.ShouldBe(typeof(TEvent));
        @event.Data.ShouldBeOfType<TEvent>();
    }
}
