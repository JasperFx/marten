#nullable enable
using System;
using System.Text.Json;
using EventSourcingTests.SchemaChange.MultipleVersions.V3;
using Marten;
using Marten.Services.Json;
using Marten.Services.Json.Transformations.SystemTextJson;
using static Marten.Events.EventMappingExtensions;

namespace EventSourcingTests.SchemaChange.MultipleVersions.Classes
{
    namespace SystemTextJson.WithTheSameName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V2.WithTheSameName.ShoppingCartOpened>
        {
            protected override V2.WithTheSameName.ShoppingCartOpened Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V2.WithTheSameName.ShoppingCartOpened(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    oldEvent.GetProperty("ClientId").GetGuid()
                );
            }
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V2.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V2.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    oldEvent.GetProperty("ProductId").GetGuid(),
                    oldEvent.GetProperty("Quantity").GetInt32()
                );
            }
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ShoppingCartOpened>
        {
            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithTheSameName.ShoppingCartOpened(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new V3.Client(oldEvent.GetProperty("ClientId").GetGuid())
                );
            }
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new ProductItem(
                        oldEvent.GetProperty("ProductId").GetGuid(),
                        oldEvent.GetProperty("Quantity").GetInt32()
                    )
                );
            }
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ShoppingCartOpened>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V3.WithTheSameName.ShoppingCartOpened>(2);

            protected override V3.WithTheSameName.ShoppingCartOpened Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithTheSameName.ShoppingCartOpened(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new V3.Client(oldEvent.GetProperty("ClientId").GetGuid()),
                    Enum.Parse<V3.ShoppingCartStatus>(oldEvent.GetProperty("Status").GetString()!),
                    oldEvent.GetProperty("OpenedAt").GetDateTime()
                );
            }
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V3.WithTheSameName.ProductItemAddedToShoppingCart>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion<V3.WithTheSameName.ProductItemAddedToShoppingCart>(2);

            protected override V3.WithTheSameName.ProductItemAddedToShoppingCart Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new ProductItem(
                        oldEvent.GetProperty("ProductId").GetGuid(),
                        oldEvent.GetProperty("Quantity").GetInt32(),
                        oldEvent.GetProperty("Price").GetDecimal()
                    )
                );
            }
        }
    }

    namespace SystemTextJson.WithDifferentName
    {
        public class ShoppingCartOpenedV1toV2Upcaster:
            EventUpcaster<V2.WithDifferentName.ShoppingCartOpenedV2>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override V2.WithDifferentName.ShoppingCartOpenedV2 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V2.WithDifferentName.ShoppingCartOpenedV2(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    oldEvent.GetProperty("ClientId").GetGuid()
                );
            }
        }

        public class ProductItemAddedToShoppingCartV1toV2Upcaster:
            EventUpcaster<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>
        {
            public override string EventTypeName => "product_item_added_to_shopping_cart";

            protected override V2.WithDifferentName.ProductItemAddedToShoppingCartV2 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    oldEvent.GetProperty("ProductId").GetGuid(),
                    oldEvent.GetProperty("Quantity").GetInt32()
                );
            }
        }

        public class ShoppingCartOpenedV1toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            public override string EventTypeName => "shopping_cart_opened";

            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithDifferentName.ShoppingCartOpenedV3(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new V3.Client(oldEvent.GetProperty("ClientId").GetGuid())
                );
            }
        }

        public class ProductItemAddedToShoppingCartV1toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            public override string EventTypeName => "product_item_added_to_shopping_cart";

            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new ProductItem(
                        oldEvent.GetProperty("ProductId").GetGuid(),
                        oldEvent.GetProperty("Quantity").GetInt32()
                    )
                );
            }
        }

        public class ShoppingCartOpenedV2toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ShoppingCartOpenedV3>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion("shopping_cart_opened", 2);

            protected override V3.WithDifferentName.ShoppingCartOpenedV3 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithDifferentName.ShoppingCartOpenedV3(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new V3.Client(oldEvent.GetProperty("ClientId").GetGuid()),
                    Enum.Parse<V3.ShoppingCartStatus>(oldEvent.GetProperty("Status").GetString()!),
                    oldEvent.GetProperty("OpenedAt").GetDateTime()
                );
            }
        }

        public class ProductItemAddedToShoppingCartV2toV3Upcaster:
            EventUpcaster<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>
        {
            public override string EventTypeName =>
                GetEventTypeNameWithSchemaVersion("product_item_added_to_shopping_cart", 2);

            protected override V3.WithDifferentName.ProductItemAddedToShoppingCartV3 Upcast(
                JsonDocument oldEventJson
            )
            {
                var oldEvent = oldEventJson.RootElement;

                return new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                    new ProductItem(
                        oldEvent.GetProperty("ProductId").GetGuid(),
                        oldEvent.GetProperty("Quantity").GetInt32(),
                        oldEvent.GetProperty("Price").GetDecimal()
                    )
                );
            }
        }
    }

    public class SystemTextJsonUpcastConfiguration
    {
        public static Action<StoreOptions> V2WithTheSameName =>
            options =>
            {
                options.UseSystemTextJsonForSerialization();
                options.Events
                    .Upcast
                    (
                        new SystemTextJson.WithTheSameName.ShoppingCartOpenedV1toV2Upcaster(),
                        new SystemTextJson.WithTheSameName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                    )
                    .MapEventTypeWithSchemaVersion<
                        V2.WithTheSameName.ShoppingCartOpened>(2)
                    .MapEventTypeWithSchemaVersion<
                        V2.WithTheSameName.ProductItemAddedToShoppingCart>(2);
            };

        public static Action<StoreOptions> V3WithTheSameName =>
            options =>
            {
                options.UseSystemTextJsonForSerialization();
                options.Events
                    .Upcast
                    (
                        new SystemTextJson.WithTheSameName.ShoppingCartOpenedV1toV3Upcaster(),
                        new SystemTextJson.WithTheSameName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                        new SystemTextJson.WithTheSameName.ShoppingCartOpenedV2toV3Upcaster(),
                        new SystemTextJson.WithTheSameName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                    )
                    .MapEventTypeWithSchemaVersion<
                        V3.WithTheSameName.ShoppingCartOpened>(3)
                    .MapEventTypeWithSchemaVersion<
                        V3.WithTheSameName.ProductItemAddedToShoppingCart>(3);
            };

        public static Action<StoreOptions> V2WithDifferentName =>
            options =>
            {
                options.UseSystemTextJsonForSerialization();
                options.Events
                    .Upcast
                    (
                        new SystemTextJson.WithDifferentName.ShoppingCartOpenedV1toV2Upcaster(),
                        new SystemTextJson.WithDifferentName.ProductItemAddedToShoppingCartV1toV2Upcaster()
                    );
            };

        public static Action<StoreOptions> V3WithDifferentName =>
            options =>
            {
                options.UseSystemTextJsonForSerialization();
                options.Events
                    .Upcast
                    (
                        new SystemTextJson.WithDifferentName.ShoppingCartOpenedV1toV3Upcaster(),
                        new SystemTextJson.WithDifferentName.ProductItemAddedToShoppingCartV1toV3Upcaster(),
                        new SystemTextJson.WithDifferentName.ShoppingCartOpenedV2toV3Upcaster(),
                        new SystemTextJson.WithDifferentName.ProductItemAddedToShoppingCartV2toV3Upcaster()
                    );
            };
    }
}
