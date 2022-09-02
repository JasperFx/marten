#nullable enable
#if NET6_0_OR_GREATER
using System;
using EventSourcingTests.SchemaChange.MultipleVersions.V3;
using Marten;
using Marten.Services.Json.Transformations.JsonNet;
using Newtonsoft.Json.Linq;
using static Marten.Events.EventMappingExtensions;

namespace EventSourcingTests.SchemaChange.MultipleVersions.Classes
{
    namespace JsonNet.WithTheSameName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V2.WithTheSameName.ShoppingCartOpened>
        {
            protected override V2.WithTheSameName.ShoppingCartOpened Upcast(
                JObject oldEvent
            ) =>
                new V2.WithTheSameName.ShoppingCartOpened(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    (Guid)oldEvent["ClientId"]!
                );
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V2.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V2.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JObject oldEvent
            ) =>
                new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    (Guid)oldEvent["ProductId"]!,
                    (int)oldEvent["Quantity"]!
                );
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ShoppingCartOpened>
        {
            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                JObject oldEvent
            ) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new V3.Client((Guid)oldEvent["ClientId"]!)
                );
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JObject oldEvent
            ) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new ProductItem(
                        (Guid)oldEvent["ProductId"]!,
                        (int)oldEvent["Quantity"]!
                    )
                );
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ShoppingCartOpened>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V3.WithTheSameName.ShoppingCartOpened>(2);

            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                JObject oldEvent
            ) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new V3.Client((Guid)oldEvent["ClientId"]!),
                    Enum.Parse<V3.ShoppingCartStatus>((string)oldEvent["Status"]!),
                    (DateTime)oldEvent["OpenedAt"]!
                );
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V3.WithTheSameName.ProductItemAddedToShoppingCart>(2);

            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JObject oldEvent
            ) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new ProductItem(
                        (Guid)oldEvent["ProductId"]!,
                        (int)oldEvent["Quantity"]!,
                        (decimal)oldEvent["Price"]!
                    )
                );
        }
    }

    namespace JsonNet.WithDifferentName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V2.WithDifferentName.ShoppingCartOpenedV2>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override V2.WithDifferentName.ShoppingCartOpenedV2 Upcast(
                JObject oldEvent
            ) =>
                new V2.WithDifferentName.ShoppingCartOpenedV2(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    (Guid)oldEvent["ClientId"]!
                );
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>
        {
            public override string EventTypeName => "product_item_added_to_shopping_cart";

            protected override V2.WithDifferentName.ProductItemAddedToShoppingCartV2 Upcast(
                JObject oldEvent
            ) =>
                new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    (Guid)oldEvent["ProductId"]!,
                    (int)oldEvent["Quantity"]!
                );
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                JObject oldEvent
            ) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new V3.Client((Guid)oldEvent["ClientId"]!)
                );
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            public override string EventTypeName => "product_item_added_to_shopping_cart";

            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                JObject oldEvent
            ) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new ProductItem(
                        (Guid)oldEvent["ProductId"]!,
                        (int)oldEvent["Quantity"]!
                    )
                );
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion("shopping_cart_opened", 2);

            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                JObject oldEvent
            ) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new V3.Client((Guid)oldEvent["ClientId"]!),
                    Enum.Parse<V3.ShoppingCartStatus>((string)oldEvent["Status"]!),
                    (DateTime)oldEvent["OpenedAt"]!
                );
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion("product_item_added_to_shopping_cart", 2);

            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                JObject oldEvent
            ) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new ProductItem(
                        (Guid)oldEvent["ProductId"]!,
                        (int)oldEvent["Quantity"]!,
                        (decimal)oldEvent["Price"]!
                    )
                );
        }
    }

    public class JsonNetUpcastConfiguration
    {
        public static Action<StoreOptions> V2WithTheSameName =>
            options =>
            {
                options.Events
                    .Upcast
                    (
                        new JsonNet.WithTheSameName.ShoppingCartOpenedV1toV2Upcaster(),
                        new JsonNet.WithTheSameName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                    )
                    .MapEventTypeWithSchemaVersion<
                        V2.WithTheSameName.ShoppingCartOpened>(2)
                    .MapEventTypeWithSchemaVersion<
                        V2.WithTheSameName.ProductItemAddedToShoppingCart>(2);
            };

        public static Action<StoreOptions> V3WithTheSameName =>
            options =>
            {
                options.Events
                    .Upcast
                    (
                        new JsonNet.WithTheSameName.ShoppingCartOpenedV1toV3Upcaster(),
                        new JsonNet.WithTheSameName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                        new JsonNet.WithTheSameName.ShoppingCartOpenedV2toV3Upcaster(),
                        new JsonNet.WithTheSameName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                    )
                    .MapEventTypeWithSchemaVersion<
                        V3.WithTheSameName.ShoppingCartOpened>(3)
                    .MapEventTypeWithSchemaVersion<
                        V3.WithTheSameName.ProductItemAddedToShoppingCart>(3);
            };

        public static Action<StoreOptions> V2WithDifferentName =>
            options =>
            {
                options.Events
                    .Upcast
                    (
                        new JsonNet.WithDifferentName.ShoppingCartOpenedV1toV2Upcaster(),
                        new JsonNet.WithDifferentName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                    );
            };

        public static Action<StoreOptions> V3WithDifferentName =>
            options =>
            {
                options.Events
                    .Upcast
                    (
                        new JsonNet.WithDifferentName.ShoppingCartOpenedV1toV3Upcaster(),
                        new JsonNet.WithDifferentName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                        new JsonNet.WithDifferentName.ShoppingCartOpenedV2toV3Upcaster(),
                        new JsonNet.WithDifferentName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                    );
            };
    }
}
#endif
