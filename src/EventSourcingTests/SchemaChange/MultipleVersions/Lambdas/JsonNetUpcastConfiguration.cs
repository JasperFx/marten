#nullable enable
using System;
using EventSourcingTests.SchemaChange.MultipleVersions.V3;
using Marten;
using static Marten.Services.Json.Transformations.JsonNet.JsonTransformations;

namespace EventSourcingTests.SchemaChange.MultipleVersions.Lambdas;

public class JsonNetUpcastConfiguration
{
    public static Action<StoreOptions> V2WithTheSameName =>
        options =>
        {
            options.Events
                .Upcast<V2.WithTheSameName.ShoppingCartOpened>(
                    Upcast(oldEvent =>
                        new V2.WithTheSameName.ShoppingCartOpened(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            (Guid)oldEvent["ClientId"]!
                        )
                    )
                )
                .Upcast<V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                    Upcast(oldEvent =>
                        new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            (Guid)oldEvent["ProductId"]!,
                            (int)oldEvent["Quantity"]!
                        ))
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
                .Upcast<V3.WithTheSameName.ShoppingCartOpened>(
                    Upcast(oldEvent =>
                        new V3.WithTheSameName.ShoppingCartOpened(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new V3.Client((Guid)oldEvent["ClientId"]!)
                        ))
                )
                .Upcast<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    Upcast(oldEvent =>
                        new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new ProductItem(
                                (Guid)oldEvent["ProductId"]!,
                                (int)oldEvent["Quantity"]!
                            )
                        ))
                )
                .Upcast<V3.WithTheSameName.ShoppingCartOpened>(
                    2, Upcast(oldEvent =>
                        new V3.WithTheSameName.ShoppingCartOpened(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new V3.Client((Guid)oldEvent["ClientId"]!),
                            Enum.Parse<V3.ShoppingCartStatus>((string)oldEvent["Status"]!),
                            (DateTime)oldEvent["OpenedAt"]!
                        ))
                )
                .Upcast<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    2, Upcast(oldEvent =>
                        new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new ProductItem(
                                (Guid)oldEvent["ProductId"]!,
                                (int)oldEvent["Quantity"]!,
                                (decimal)oldEvent["Price"]!
                            )
                        ))
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
                .Upcast<V2.WithDifferentName.ShoppingCartOpenedV2>(
                    "shopping_cart_opened",
                    Upcast(oldEvent =>
                        new V2.WithDifferentName.ShoppingCartOpenedV2(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            (Guid)oldEvent["ClientId"]!
                        ))
                )
                .Upcast<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                    "product_item_added_to_shopping_cart",
                    Upcast(oldEvent =>
                        new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            (Guid)oldEvent["ProductId"]!,
                            (int)oldEvent["Quantity"]!
                        ))
                );
        };

    public static Action<StoreOptions> V3WithDifferentName =>
        options =>
        {
            options.Events
                .Upcast<V3.WithDifferentName.ShoppingCartOpenedV3>(
                    "shopping_cart_opened",
                    Upcast(oldEvent =>
                        new V3.WithDifferentName.ShoppingCartOpenedV3(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new V3.Client((Guid)oldEvent["ClientId"]!)
                        ))
                )
                .Upcast<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart",
                    Upcast(oldEvent =>
                        new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new ProductItem(
                                (Guid)oldEvent["ProductId"]!,
                                (int)oldEvent["Quantity"]!
                            )
                        ))
                )
                .Upcast<V3.WithDifferentName.ShoppingCartOpenedV3>(
                    "shopping_cart_opened_v2",
                    Upcast(oldEvent =>
                        new V3.WithDifferentName.ShoppingCartOpenedV3(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new V3.Client((Guid)oldEvent["ClientId"]!),
                            Enum.Parse<V3.ShoppingCartStatus>((string)oldEvent["Status"]!),
                            (DateTime)oldEvent["OpenedAt"]!
                        ))
                )
                .Upcast<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart_v2",
                    Upcast(oldEvent =>
                        new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                            (Guid)oldEvent["ShoppingCartId"]!,
                            new ProductItem(
                                (Guid)oldEvent["ProductId"]!,
                                (int)oldEvent["Quantity"]!,
                                (decimal)oldEvent["Price"]!
                            )
                        ))
                );
        };
}
