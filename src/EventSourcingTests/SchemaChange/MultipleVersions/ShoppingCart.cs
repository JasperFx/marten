#nullable enable
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;

namespace EventSourcingTests.SchemaChange.MultipleVersions
{
    namespace V1
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

    namespace V2
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

    namespace V3
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
}
#endif
