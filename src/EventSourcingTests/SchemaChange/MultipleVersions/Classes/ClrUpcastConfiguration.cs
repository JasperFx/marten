#nullable enable
using System;
using Marten;
using Marten.Services.Json.Transformations;
using static Marten.Events.EventMappingExtensions;

namespace EventSourcingTests.SchemaChange.MultipleVersions.Classes
{
    namespace Clr.WithTheSameName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V1.ShoppingCartOpened, V2.WithTheSameName.ShoppingCartOpened>
        {
            protected override V2.WithTheSameName.ShoppingCartOpened Upcast(
                V1.ShoppingCartOpened @event
            ) =>
                new V2.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    @event.ClientId
                );
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V1.ProductItemAddedToShoppingCart, V2.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V2.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                V1.ProductItemAddedToShoppingCart @event
            ) =>
                new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    @event.ProductId,
                    @event.Quantity
                );
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V1.ShoppingCartOpened, V3.WithTheSameName.ShoppingCartOpened>
        {
            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                V1.ShoppingCartOpened @event
            ) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                );
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V1.ProductItemAddedToShoppingCart, V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                V1.ProductItemAddedToShoppingCart @event
            ) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity)
                );
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V2.WithTheSameName.ShoppingCartOpened, V3.WithTheSameName.ShoppingCartOpened>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V1.ShoppingCartOpened>(2);

            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                V2.WithTheSameName.ShoppingCartOpened @event
            ) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId),
                    (V3.ShoppingCartStatus)(int)@event.Status,
                    @event.OpenedAt
                );
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V2.WithTheSameName.ProductItemAddedToShoppingCart,
                V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V1.ProductItemAddedToShoppingCart>(2);

            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                V2.WithTheSameName.ProductItemAddedToShoppingCart @event
            ) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                );
        }
    }

    namespace Clr.WithDifferentName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V1.ShoppingCartOpened, V2.WithDifferentName.ShoppingCartOpenedV2>
        {
            protected override V2.WithDifferentName.ShoppingCartOpenedV2 Upcast(
                V1.ShoppingCartOpened @event
            ) =>
                new V2.WithDifferentName.ShoppingCartOpenedV2(
                    @event.ShoppingCartId,
                    @event.ClientId
                );
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V1.ProductItemAddedToShoppingCart, V2.WithDifferentName.ProductItemAddedToShoppingCartV2>
        {
            protected override V2.WithDifferentName.ProductItemAddedToShoppingCartV2 Upcast(
                V1.ProductItemAddedToShoppingCart @event
            ) =>
                new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                    @event.ShoppingCartId,
                    @event.ProductId,
                    @event.Quantity
                );
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V1.ShoppingCartOpened, V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                V1.ShoppingCartOpened @event
            ) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                );
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V1.ProductItemAddedToShoppingCart, V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                V1.ProductItemAddedToShoppingCart @event
            ) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity)
                );
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V2.WithDifferentName.ShoppingCartOpenedV2, V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                V2.WithDifferentName.ShoppingCartOpenedV2 @event
            ) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId),
                    (V3.ShoppingCartStatus)(int)@event.Status,
                    @event.OpenedAt
                );
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V2.WithDifferentName.ProductItemAddedToShoppingCartV2,
                V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                V2.WithDifferentName.ProductItemAddedToShoppingCartV2 @event
            ) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                );
        }
    }

    public class ClrUpcastConfiguration
    {
        public static Action<StoreOptions> V2WithTheSameName =>
            options => options.Events
                .Upcast
                (
                    new Clr.WithTheSameName.ShoppingCartOpenedV1toV2Upcaster(),
                    new Clr.WithTheSameName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                )
                .MapEventTypeWithSchemaVersion<
                    V2.WithTheSameName.ShoppingCartOpened>(2)
                .MapEventTypeWithSchemaVersion<
                    V2.WithTheSameName.ProductItemAddedToShoppingCart>(2);

        public static Action<StoreOptions> V3WithTheSameName =>
            options => options.Events
                .Upcast
                (
                    new Clr.WithTheSameName.ShoppingCartOpenedV1toV3Upcaster(),
                    new Clr.WithTheSameName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                    new Clr.WithTheSameName.ShoppingCartOpenedV2toV3Upcaster(),
                    new Clr.WithTheSameName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                )
                .MapEventTypeWithSchemaVersion<
                    V3.WithTheSameName.ShoppingCartOpened>(3)
                .MapEventTypeWithSchemaVersion<
                    V3.WithTheSameName.ProductItemAddedToShoppingCart>(3);

        public static Action<StoreOptions> V2WithDifferentName =>
            options => options.Events
                .Upcast
                (
                    new Clr.WithDifferentName.ShoppingCartOpenedV1toV2Upcaster(),
                    new Clr.WithDifferentName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                );

        public static Action<StoreOptions> V3WithDifferentName =>
            options => options.Events
                .Upcast
                (
                    new Clr.WithDifferentName.ShoppingCartOpenedV1toV3Upcaster(),
                    new Clr.WithDifferentName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                    new Clr.WithDifferentName.ShoppingCartOpenedV2toV3Upcaster(),
                    new Clr.WithDifferentName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                );
    }
}
