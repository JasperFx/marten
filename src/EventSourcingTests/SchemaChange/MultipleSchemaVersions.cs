#nullable enable
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.SchemaChange.MultipleVersions.V3;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

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
            Guid ShoppingCartId,
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
            UnderFraudDetection = 1,
            Opened = 2,
            Confirmed = 3,
            Cancelled = 4
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
                Client Client,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpenedV3 @event) =>
                    new ShoppingCart(
                        @event.CartId,
                        @event.Client,
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

        namespace WithDifferentName
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
                Client Client,
                Dictionary<Guid, int> ProductItems,
                ShoppingCartStatus Status = ShoppingCartStatus.Opened,
                DateTime? OpenedAt = null
            )
            {
                public static ShoppingCart Create(ShoppingCartOpened @event) =>
                    new ShoppingCart(
                        @event.CartId,
                        @event.Client,
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
    }

    public class MultipleSchemaVersions: OneOffConfigurationsContext
    {
        [Fact]
        public async Task Sth()
        {
            // test events data
            var shoppingCartId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var initialQuantity = 3;
            var additionalQuantity = 2;
            var andMoreQuantity = 4;

            StoreOptions(options => options.GeneratedCodeMode = TypeLoadMode.Auto);
            await theStore.EnsureStorageExistsAsync(typeof(StreamAction));

            await using (var session = theStore.OpenSession())
            {
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V1.ShoppingCartOpened(shoppingCartId, clientId));
                session.Events.Append(shoppingCartId,
                    new MultipleVersions.V1.ProductItemAddedToShoppingCart(shoppingCartId, productId, initialQuantity));
                await session.SaveChangesAsync();
            }

            using var store = SeparateStore(options =>
            {
                options.GeneratedCodeMode = TypeLoadMode.Auto;
                options.Events
                    .Upcast<MultipleVersions.V1.ShoppingCartOpened,
                        MultipleVersions.V3.WithDifferentName.ShoppingCartOpened>(@event =>
                        new MultipleVersions.V3.WithDifferentName.ShoppingCartOpened(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast<MultipleVersions.V1.ProductItemAddedToShoppingCart,
                        MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCart>(@event =>
                        new MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCart(
                            @event.ShoppingCartId,
                            new ProductItem(@event.ProductId, @event.Quantity)
                        )
                    )
                    .Upcast<MultipleVersions.V2.WithDifferentName.ShoppingCartOpenedV2,
                        MultipleVersions.V3.WithDifferentName.ShoppingCartOpened>(@event =>
                        new MultipleVersions.V3.WithDifferentName.ShoppingCartOpened(
                            @event.ShoppingCartId,
                            new MultipleVersions.V3.Client(@event.ClientId)
                        )
                    )
                    .Upcast<MultipleVersions.V2.WithDifferentName.ProductItemAddedToShoppingCartV2,
                        MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCart>(@event =>
                        new MultipleVersions.V3.WithDifferentName.ProductItemAddedToShoppingCart(
                            @event.ShoppingCartId,
                            new ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                        )
                    );
            });
            {
                await using var session = store.OpenSession();
                var shoppingCartNew =
                    await session.Events.AggregateStreamAsync<MultipleVersions.V3.WithDifferentName.ShoppingCart>(
                        shoppingCartId);

                shoppingCartNew.Id.ShouldBe(shoppingCartId);
                shoppingCartNew.Client.ShouldNotBeNull();
                shoppingCartNew.Client.Id.ShouldBe(clientId);
            }
        }
    }
}
#endif
