#nullable enable
#if NET6_0_OR_GREATER
using System;
using Marten;

namespace EventSourcingTests.SchemaChange.MultipleVersions;

public class ClrUpcastConfiguration
{
    public static Action<StoreOptions> V2WithTheSameName =>
        options => options.Events
            .Upcast((V1.ShoppingCartOpened @event) =>
                new V2.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    @event.ClientId
                )
            )
            .Upcast((V1.ProductItemAddedToShoppingCart @event) =>
                new V2.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    @event.ProductId,
                    @event.Quantity
                )
            )
            .MapEventTypeWithSchemaVersion<
                V2.WithTheSameName.ShoppingCartOpened>(2)
            .MapEventTypeWithSchemaVersion<
                V2.WithTheSameName.ProductItemAddedToShoppingCart>(2);

    public static Action<StoreOptions> V3WithTheSameName =>
        options => options.Events
            .Upcast((V1.ShoppingCartOpened @event) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                )
            )
            .Upcast((V1.ProductItemAddedToShoppingCart @event) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity)
                )
            )
            .Upcast(2, (V2.WithTheSameName.ShoppingCartOpened @event) =>
                new V3.WithTheSameName.ShoppingCartOpened(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                )
            )
            .Upcast(2, (V2.WithTheSameName.ProductItemAddedToShoppingCart @event) =>
                new V3.WithTheSameName.ProductItemAddedToShoppingCart(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                )
            )
            .MapEventTypeWithSchemaVersion<
                V3.WithTheSameName.ShoppingCartOpened>(3)
            .MapEventTypeWithSchemaVersion<
                V3.WithTheSameName.ProductItemAddedToShoppingCart>(3);

    public static Action<StoreOptions> V2WithDifferentName =>
        options => options.Events
            .Upcast((V1.ShoppingCartOpened @event) =>
                new V2.WithDifferentName.ShoppingCartOpenedV2(
                    @event.ShoppingCartId,
                    @event.ClientId
                )
            )
            .Upcast((V1.ProductItemAddedToShoppingCart @event) =>
                new V2.WithDifferentName.ProductItemAddedToShoppingCartV2(
                    @event.ShoppingCartId,
                    @event.ProductId,
                    @event.Quantity
                )
            );

    public static Action<StoreOptions> V3WithDifferentName =>
        options => options.Events
            .Upcast((V1.ShoppingCartOpened @event) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                )
            )
            .Upcast((V1.ProductItemAddedToShoppingCart @event) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity)
                )
            )
            .Upcast((V2.WithDifferentName.ShoppingCartOpenedV2 @event) =>
                new V3.WithDifferentName.ShoppingCartOpenedV3(
                    @event.ShoppingCartId,
                    new V3.Client(@event.ClientId)
                )
            )
            .Upcast((V2.WithDifferentName.ProductItemAddedToShoppingCartV2 @event) =>
                new V3.WithDifferentName.ProductItemAddedToShoppingCartV3(
                    @event.ShoppingCartId,
                    new V3.ProductItem(@event.ProductId, @event.Quantity, @event.Price)
                )
            );
}
#endif
