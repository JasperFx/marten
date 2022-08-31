#nullable enable
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using static Marten.Events.EventMappingExtensions;

namespace EventSourcingTests.SchemaChange
{
    namespace MultipleVersions.V1
    {
        public record ShoppingCartOpened(
            Guid ShoppingCartId,
            Guid ClientId
        );

        public record ProductItemAddedToShoppingCart(
            Guid ShoppingCartId,
            Guid ProductId,
            int Quantity
        );

        public record ShoppingCart(
            Guid Id,
            Guid ClientId,
            Dictionary<Guid, int> ProductItems
        )
        {
            public static ShoppingCart Create(ShoppingCartOpened @event) =>
                new ShoppingCart(@event.ShoppingCartId, @event.ClientId, new Dictionary<Guid, int>());

            public ShoppingCart Apply(ProductItemAddedToShoppingCart @event) =>
                this with
                {
                    ProductItems = ProductItems.Select(x => new { ProductId = x.Key, Quantity = x.Value })
                        .Union(new[] { new { @event.ProductId, @event.Quantity } })
                        .GroupBy(x => x.ProductId)
                        .ToDictionary(x => x.Key, x => x.Sum(k => k.Quantity))
                };
        }
    }

    namespace MultipleVersions.V2
    {
        public enum ShoppingCartStatus
        {
            Opened,
            UnderFraudDetection,
            Confirmed,
            Cancelled
        }

        namespace WithTheSameName
        {
            public record ShoppingCartOpened(
                Guid ShoppingCartId,
                Guid ClientId,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            );

            public record ProductItemAddedToShoppingCart(
                Guid ShoppingCartId,
                Guid ProductId,
                int Quantity,
                decimal? Price = null
            );

            public record ShoppingCart(
                Guid Id,
                Guid ClientId,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpened @event) =>
                    new ShoppingCart(
                        @event.ShoppingCartId,
                        @event.ClientId,
                        new Dictionary<Guid, int>(),
                        @event.Status,
                        @event.OpenedAt
                    );

                public ShoppingCart Apply(ProductItemAddedToShoppingCart @event) =>
                    this with
                    {
                        ProductItems = ProductItems.Select(x => new { ProductId = x.Key, Quantity = x.Value })
                            .Union(new[] { new { @event.ProductId, @event.Quantity } })
                            .GroupBy(x => x.ProductId)
                            .ToDictionary(x => x.Key, x => x.Sum(k => k.Quantity))
                    };
            }
        }

        namespace WithDifferentName
        {
            public record ShoppingCartOpenedV2(
                Guid ShoppingCartId,
                Guid ClientId,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            );

            public record ProductItemAddedToShoppingCartV2(
                Guid ShoppingCartId,
                Guid ProductId,
                int Quantity,
                decimal? Price = null
            );

            public record ShoppingCart(
                Guid Id,
                Guid ClientId,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpenedV2 @event) =>
                    new ShoppingCart(
                        @event.ShoppingCartId,
                        @event.ClientId,
                        new Dictionary<Guid, int>(),
                        @event.Status,
                        @event.OpenedAt
                    );

                public ShoppingCart Apply(ProductItemAddedToShoppingCartV2 @event) =>
                    this with
                    {
                        ProductItems = ProductItems.Select(x => new { ProductId = x.Key, Quantity = x.Value })
                            .Union(new[] { new { @event.ProductId, @event.Quantity } })
                            .GroupBy(x => x.ProductId)
                            .ToDictionary(x => x.Key, x => x.Sum(k => k.Quantity))
                    };
            }
        }
    }

    namespace MultipleVersions.V3
    {
        public enum ShoppingCartStatus
        {
            Opened,
            UnderFraudDetection,
            Confirmed,
            Cancelled
        }

        public record Client(
            Guid Id,
            string Name = "Unknown"
        );

        public record ProductItem(
            Guid ProductId,
            int Quantity,
            decimal? Price = null
        );

        namespace WithTheSameName
        {
            public record ShoppingCartOpened(
                Guid CartId,
                Client Client,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            );

            public record ProductItemAddedToShoppingCart(
                Guid ShoppingCartId,
                ProductItem ProductItem
            );

            public record ShoppingCart(
                Guid Id,
                Guid ClientId,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpened @event) =>
                    new ShoppingCart(
                        @event.CartId,
                        @event.Client.Id,
                        new Dictionary<Guid, int>(),
                        @event.Status,
                        @event.OpenedAt
                    );

                public ShoppingCart Apply(ProductItemAddedToShoppingCart @event) =>
                    this with
                    {
                        ProductItems = ProductItems.Select(x => new { ProductId = x.Key, Quantity = x.Value })
                            .Union(new[] { new { @event.ProductItem.ProductId, @event.ProductItem.Quantity } })
                            .GroupBy(x => x.ProductId)
                            .ToDictionary(x => x.Key, x => x.Sum(k => k.Quantity))
                    };
            }
        }

        namespace WithDifferentName
        {
            public record ShoppingCartOpenedV3(
                Guid CartId,
                Client Client,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            );

            public record ProductItemAddedToShoppingCartV3(
                Guid ShoppingCartId,
                ProductItem ProductItem
            );

            public record ShoppingCart(
                Guid Id,
                Guid ClientId,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpenedV3 @event) =>
                    new ShoppingCart(
                        @event.CartId,
                        @event.Client.Id,
                        new Dictionary<Guid, int>(),
                        @event.Status,
                        @event.OpenedAt
                    );

                public ShoppingCart Apply(ProductItemAddedToShoppingCartV3 @event) =>
                    this with
                    {
                        ProductItems = ProductItems.Select(x => new { ProductId = x.Key, Quantity = x.Value })
                            .Union(new[] { new { @event.ProductItem.ProductId, @event.ProductItem.Quantity } })
                            .GroupBy(x => x.ProductId)
                            .ToDictionary(x => x.Key, x => x.Sum(k => k.Quantity))
                    };
            }
        }
    }

    public class MultipleSchemaVersions: OneOffConfigurationsContext
    {
        [Fact]
        public async Task UpcastingWithMultipleSchemaAndDifferentTypeNamesShouldWork()
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
                options.Projections.SelfAggregate<MultipleVersions.V1.ShoppingCart>();
            });
            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await AppendEventsInV1Schema(
                shoppingCartId,
                clientId,
                productId,
                currentQuantity
            );

            currentQuantity = await CheckV2WithDifferentNameUpcasting(
                shoppingCartId,
                clientId,
                productId,
                currentQuantity,
                price
            );

            await CheckV3WithDifferentNameUpcasting(
                shoppingCartId,
                clientId,
                productId,
                currentQuantity,
                price
            );
        }


        [Fact]
        public async Task UpcastingWithMultipleSchemaAndTheSameTypeNamesShouldWork()
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
                options.Projections.SelfAggregate<MultipleVersions.V1.ShoppingCart>();
            });
            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await AppendEventsInV1Schema(
                shoppingCartId,
                clientId,
                productId,
                currentQuantity
            );

            currentQuantity = await CheckV2WithTheSameNameUpcasting(
                shoppingCartId,
                clientId,
                productId,
                currentQuantity,
                price
            );

            await CheckV3WithTheSameNameUpcasting(
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
            await using var session = theStore.OpenSession();
            session.Events.Append(shoppingCartId,
                new MultipleVersions.V1.ShoppingCartOpened(shoppingCartId, clientId));
            session.Events.Append(shoppingCartId,
                new MultipleVersions.V1.ProductItemAddedToShoppingCart(shoppingCartId, productId, initialQuantity));
            await session.SaveChangesAsync();
        }

        private async Task<int> CheckV2WithTheSameNameUpcasting(Guid shoppingCartId,
            Guid clientId,
            Guid productId,
            int currentQuantity,
            decimal price)
        {
            using var storeV2 = SeparateStore(options =>
            {
                options.GeneratedCodeMode = TypeLoadMode.Auto;
                options.Projections.SelfAggregate<MultipleVersions.V2.WithTheSameName.ShoppingCart>();
                ////////////////////////////////////////////////////////
                // 2.1. Define Upcast methods from V1 to V2
                ////////////////////////////////////////////////////////
                options.Events
                    .Upcast((MultipleVersions.V1.ShoppingCartOpened @event) =>
                        new MultipleVersions.V2.WithTheSameName.ShoppingCartOpened(
                            @event.ShoppingCartId,
                            @event.ClientId
                        )
                    )
                    .Upcast((MultipleVersions.V1.ProductItemAddedToShoppingCart @event) =>
                        new MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart(
                            @event.ShoppingCartId,
                            @event.ProductId,
                            @event.Quantity
                        )
                    )
                    .MapEventTypeWithSchemaVersion<
                        MultipleVersions.V2.WithTheSameName.ShoppingCartOpened>(2)
                    .MapEventTypeWithSchemaVersion<
                        MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart>(2);
            });
            {
                await using var session = storeV2.OpenSession();

                ////////////////////////////////////////////////////////
                // 2.2. Append Event with V2 schema
                ////////////////////////////////////////////////////////
                var additionalQuantity = 2;
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart(
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
                events[0].ShouldBeOfType<MultipleVersions.V2.WithTheSameName.ShoppingCartOpened>(
                    "shopping_cart_opened"
                );
                events[1].ShouldBeOfType<MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                    "product_item_added_to_shopping_cart"
                );
                events[2].ShouldBeOfType<MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                    "product_item_added_to_shopping_cart_v2"
                );

                ////////////////////////////////////////////////////////
                // 2.4. Ensure that aggregation is using V2 schema
                ////////////////////////////////////////////////////////
                var shoppingCartV2 =
                    await session.Events.AggregateStreamAsync<MultipleVersions.V2.WithTheSameName.ShoppingCart>(
                        shoppingCartId
                    );

                var shoppingCartV2Projection =
                    await session.LoadAsync<MultipleVersions.V2.WithTheSameName.ShoppingCart>(shoppingCartId);

                shoppingCartV2
                    .ShouldNotBeNull()
                    .ShouldBeEquivalentTo(
                        new MultipleVersions.V2.WithTheSameName.ShoppingCart(
                            shoppingCartId,
                            clientId,
                            new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                            MultipleVersions.V2.ShoppingCartStatus.Opened,
                            null
                        )
                    );

                shoppingCartV2Projection.ShouldBeEquivalentTo(shoppingCartV2);

                return currentQuantity + additionalQuantity;
            }
        }

        private async Task CheckV3WithTheSameNameUpcasting(
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
                options.Projections.SelfAggregate<MultipleVersions.V3.WithTheSameName.ShoppingCart>();
                ////////////////////////////////////////////////////////
                // 3.1. Define Upcast methods from V1 to V3, and from V2 to V3
                ////////////////////////////////////////////////////////
                options.Events
                    .Upcast((MultipleVersions.V1.ShoppingCartOpened @event) =>
                        new MultipleVersions.V3.WithTheSameName.ShoppingCartOpened(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast((MultipleVersions.V1.ProductItemAddedToShoppingCart @event) =>
                        new MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.ProductItem(@event.ProductId, @event.Quantity)
                        )
                    )
                    .Upcast(2, (MultipleVersions.V2.WithTheSameName.ShoppingCartOpened @event) =>
                        new MultipleVersions.V3.WithTheSameName.ShoppingCartOpened(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast(2, (MultipleVersions.V2.WithTheSameName.ProductItemAddedToShoppingCart @event) =>
                        new MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                        )
                    )
                    .MapEventTypeWithSchemaVersion<
                        MultipleVersions.V3.WithTheSameName.ShoppingCartOpened>(3)
                    .MapEventTypeWithSchemaVersion<
                        MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart>(3);
            });
            {
                await using var session = storeV3.OpenSession();

                ////////////////////////////////////////////////////////
                // 3.2. Append Event with V3 schema
                ////////////////////////////////////////////////////////
                var additionalQuantity = 4;
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart(
                        shoppingCartId,
                        new MultipleVersions.V3.ProductItem(
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
                events[0].ShouldBeOfType<MultipleVersions.V3.WithTheSameName.ShoppingCartOpened>(
                    "shopping_cart_opened"
                );
                events[1].ShouldBeOfType<MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    "product_item_added_to_shopping_cart"
                );
                events[2].ShouldBeOfType<MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    "product_item_added_to_shopping_cart_v2"
                );
                events[3].ShouldBeOfType<MultipleVersions.V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    "product_item_added_to_shopping_cart_v3"
                );

                ////////////////////////////////////////////////////////
                // 3.4. Ensure that aggregation is using V3 schema
                ////////////////////////////////////////////////////////
                var shoppingCartV3 =
                    await session.Events.AggregateStreamAsync<MultipleVersions.V3.WithTheSameName.ShoppingCart>(
                        shoppingCartId
                    );

                var shoppingCartV3Projection =
                    await session.LoadAsync<MultipleVersions.V3.WithTheSameName.ShoppingCart>(shoppingCartId);

                shoppingCartV3
                    .ShouldNotBeNull()
                    .ShouldBeEquivalentTo(
                        new MultipleVersions.V3.WithTheSameName.ShoppingCart(
                            shoppingCartId,
                            clientId,
                            new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                            MultipleVersions.V3.ShoppingCartStatus.Opened,
                            null
                        )
                    );

                shoppingCartV3Projection.ShouldBeEquivalentTo(shoppingCartV3);
            }
        }

        private async Task<int> CheckV2WithDifferentNameUpcasting(Guid shoppingCartId,
            Guid clientId,
            Guid productId,
            int currentQuantity,
            decimal price)
        {
            using var storeV2 = SeparateStore(options =>
            {
                options.GeneratedCodeMode = TypeLoadMode.Auto;
                options.Projections.SelfAggregate<MultipleVersions.V2.WithDifferentName.ShoppingCart>();
                ////////////////////////////////////////////////////////
                // 2.1. Define Upcast methods from V1 to V2
                ////////////////////////////////////////////////////////
                options.Events
                    .Upcast((MultipleVersions.V1.ShoppingCartOpened @event) =>
                        new MultipleVersions.V2.WithDifferentName.ShoppingCartOpenedV2(
                            @event.ShoppingCartId,
                            @event.ClientId
                        )
                    )
                    .Upcast((MultipleVersions.V1.ProductItemAddedToShoppingCart @event) =>
                        new MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                            @event.ShoppingCartId,
                            @event.ProductId,
                            @event.Quantity
                        )
                    );
            });
            {
                await using var session = storeV2.OpenSession();

                ////////////////////////////////////////////////////////
                // 2.2. Append Event with V2 schema
                ////////////////////////////////////////////////////////
                var additionalQuantity = 2;
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
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
                events[0].ShouldBeOfType<MultipleVersions.V2.WithDifferentName.ShoppingCartOpenedV2>(
                    "shopping_cart_opened"
                );
                events[1].ShouldBeOfType<MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                    "product_item_added_to_shopping_cart"
                );
                events[2].ShouldBeOfType<MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                    "product_item_added_to_shopping_cart_v2"
                );

                ////////////////////////////////////////////////////////
                // 2.4. Ensure that aggregation is using V2 schema
                ////////////////////////////////////////////////////////
                var shoppingCartV2 =
                    await session.Events.AggregateStreamAsync<MultipleVersions.V2.WithDifferentName.ShoppingCart>(
                        shoppingCartId
                    );

                var shoppingCartV2Projection =
                    await session.LoadAsync<MultipleVersions.V2.WithDifferentName.ShoppingCart>(shoppingCartId);

                shoppingCartV2
                    .ShouldNotBeNull()
                    .ShouldBeEquivalentTo(
                        new MultipleVersions.V2.WithDifferentName.ShoppingCart(
                            shoppingCartId,
                            clientId,
                            new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                            MultipleVersions.V2.ShoppingCartStatus.Opened,
                            null
                        )
                    );

                shoppingCartV2Projection.ShouldBeEquivalentTo(shoppingCartV2);

                return currentQuantity + additionalQuantity;
            }
        }

        private async Task CheckV3WithDifferentNameUpcasting(
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
                options.Projections.SelfAggregate<MultipleVersions.V3.WithDifferentName.ShoppingCart>();
                ////////////////////////////////////////////////////////
                // 3.1. Define Upcast methods from V1 to V3, and from V2 to V3
                ////////////////////////////////////////////////////////
                options.Events
                    .Upcast((MultipleVersions.V1.ShoppingCartOpened @event) =>
                        new MultipleVersions.V3.WithDifferentName.ShoppingCartOpenedV3(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast((MultipleVersions.V1.ProductItemAddedToShoppingCart @event) =>
                        new MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.ProductItem(@event.ProductId, @event.Quantity)
                        )
                    )
                    .Upcast((MultipleVersions.V2.WithDifferentName.ShoppingCartOpenedV2 @event) =>
                        new MultipleVersions.V3.WithDifferentName.ShoppingCartOpenedV3(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast((MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2 @event) =>
                        new MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                        )
                    );
            });
            {
                await using var session = storeV3.OpenSession();

                ////////////////////////////////////////////////////////
                // 3.2. Append Event with V3 schema
                ////////////////////////////////////////////////////////
                var additionalQuantity = 4;
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                        shoppingCartId,
                        new MultipleVersions.V3.ProductItem(
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
                events[0].ShouldBeOfType<MultipleVersions.V3.WithDifferentName.ShoppingCartOpenedV3>(
                    "shopping_cart_opened"
                );
                events[1].ShouldBeOfType<MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart"
                );
                events[2].ShouldBeOfType<MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart_v2"
                );
                events[3].ShouldBeOfType<MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart_v3"
                );

                ////////////////////////////////////////////////////////
                // 3.4. Ensure that aggregation is using V3 schema
                ////////////////////////////////////////////////////////
                var shoppingCartV3 =
                    await session.Events.AggregateStreamAsync<MultipleVersions.V3.WithDifferentName.ShoppingCart>(
                        shoppingCartId
                    );

                var shoppingCartV3Projection =
                    await session.LoadAsync<MultipleVersions.V3.WithDifferentName.ShoppingCart>(shoppingCartId);

                shoppingCartV3
                    .ShouldNotBeNull()
                    .ShouldBeEquivalentTo(
                        new MultipleVersions.V3.WithDifferentName.ShoppingCart(
                            shoppingCartId,
                            clientId,
                            new Dictionary<Guid, int> { { productId, currentQuantity + additionalQuantity } },
                            MultipleVersions.V3.ShoppingCartStatus.Opened,
                            null
                        )
                    );

                shoppingCartV3Projection.ShouldBeEquivalentTo(shoppingCartV3);
            }
        }
    }
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
#endif
