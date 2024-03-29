#nullable enable
using System;
using EventSourcingTests.SchemaChange.MultipleVersions.V3;
using Marten;
using Marten.Services.Json;
using static Marten.Services.Json.Transformations.SystemTextJson.JsonTransformations;

namespace EventSourcingTests.SchemaChange.MultipleVersions.Lambdas;

public class SystemTextJsonUpcastConfiguration
{
    public static Action<StoreOptions> V2WithTheSameName =>
        options =>
        {
            options.UseSystemTextJsonForSerialization();
            options.Events
                .Upcast<V2.WithTheSameName.ShoppingCartOpened>(
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V2.WithTheSameName.ShoppingCartOpened(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            oldEvent.GetProperty("ClientId").GetGuid()
                        );
                    })
                )
                .Upcast<V2.WithTheSameName.ProductItemAddedToShoppingCart>(
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            oldEvent.GetProperty("ProductId").GetGuid(),
                            oldEvent.GetProperty("Quantity").GetInt32()
                        );
                    })
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
                .Upcast<V3.WithTheSameName.ShoppingCartOpened>(
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithTheSameName.ShoppingCartOpened(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new V3.Client(oldEvent.GetProperty("ClientId").GetGuid())
                        );
                    })
                )
                .Upcast<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new ProductItem(
                                oldEvent.GetProperty("ProductId").GetGuid(),
                                oldEvent.GetProperty("Quantity").GetInt32()
                            )
                        );
                    })
                )
                .Upcast<V3.WithTheSameName.ShoppingCartOpened>(
                    2, Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithTheSameName.ShoppingCartOpened(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new V3.Client(oldEvent.GetProperty("ClientId").GetGuid()),
                            Enum.Parse<V3.ShoppingCartStatus>(oldEvent.GetProperty("Status").GetString()!),
                            oldEvent.GetProperty("OpenedAt").GetDateTime()
                        );
                    })
                )
                .Upcast<V3.WithTheSameName.ProductItemAddedToShoppingCart>(
                    2, Upcast(oldEventJson =>
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
                    })
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
                .Upcast<V2.WithDifferentName.ShoppingCartOpenedV2>(
                    "shopping_cart_opened",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V2.WithDifferentName.ShoppingCartOpenedV2(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            oldEvent.GetProperty("ClientId").GetGuid()
                        );
                    })
                )
                .Upcast<V2.WithDifferentName.ProductItemAddedToShoppingCartV2>(
                    "product_item_added_to_shopping_cart",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            oldEvent.GetProperty("ProductId").GetGuid(),
                            oldEvent.GetProperty("Quantity").GetInt32()
                        );
                    })
                );
        };

    public static Action<StoreOptions> V3WithDifferentName =>
        options =>
        {
            options.UseSystemTextJsonForSerialization();
            options.Events
                .Upcast<V3.WithDifferentName.ShoppingCartOpenedV3>(
                    "shopping_cart_opened",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithDifferentName.ShoppingCartOpenedV3(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new V3.Client(oldEvent.GetProperty("ClientId").GetGuid())
                        );
                    })
                )
                .Upcast<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new ProductItem(
                                oldEvent.GetProperty("ProductId").GetGuid(),
                                oldEvent.GetProperty("Quantity").GetInt32()
                            )
                        );
                    })
                )
                .Upcast<V3.WithDifferentName.ShoppingCartOpenedV3>(
                    "shopping_cart_opened_v2",
                    Upcast(oldEventJson =>
                    {
                        var oldEvent = oldEventJson.RootElement;

                        return new V3.WithDifferentName.ShoppingCartOpenedV3(
                            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                            new V3.Client(oldEvent.GetProperty("ClientId").GetGuid()),
                            Enum.Parse<V3.ShoppingCartStatus>(oldEvent.GetProperty("Status").GetString()!),
                            oldEvent.GetProperty("OpenedAt").GetDateTime()
                        );
                    })
                )
                .Upcast<V3.WithDifferentName.ProductItemAddedToShoppingCartV3>(
                    "product_item_added_to_shopping_cart_v2",
                    Upcast(oldEventJson =>
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
                    })
                );
        };
}
