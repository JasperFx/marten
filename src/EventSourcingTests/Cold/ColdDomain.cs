using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Aggregation;

namespace EventSourcingTests.Cold;

public record CartOpened(string Customer);

public record ItemAdded(string Name);

public record CartCheckedOut;

/// <summary>An old shape of <see cref="ItemAdded" />, still in cold storage.</summary>
public record ItemAddedV1(string Item);

/// <summary>Folded with conventional Create/Apply methods.</summary>
public class Cart
{
    public string Id { get; set; } = null!;
    public string Customer { get; set; } = null!;
    public List<string> Items { get; set; } = new();
    public bool CheckedOut { get; set; }

    public static Cart Create(CartOpened e) => new() { Customer = e.Customer };
    public void Apply(ItemAdded e) => Items.Add(e.Name);
    public void Apply(CartCheckedOut _) => CheckedOut = true;
}

public class GuidCart
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = null!;
    public List<string> Items { get; set; } = new();

    public static GuidCart Create(CartOpened e) => new() { Customer = e.Customer };
    public void Apply(ItemAdded e) => Items.Add(e.Name);
}

public abstract class CartSummary
{
    public string Id { get; set; } = null!;
    public string Customer { get; set; } = null!;
    public List<string> Items { get; set; } = new();

    public void Fold(IEvent e)
    {
        switch (e.Data)
        {
            case CartOpened opened:
                Customer = opened.Customer;
                break;
            case ItemAdded added:
                Items.Add(added.Name);
                break;
        }
    }
}

public class EvolvedCart: CartSummary;

public class AsyncEvolvedCart: CartSummary;

public class DecidedCart: CartSummary;

public partial class EvolvedCartProjection: SingleStreamProjection<EvolvedCart, string>
{
    public override EvolvedCart Evolve(EvolvedCart snapshot, string id, IEvent e)
    {
        snapshot ??= new EvolvedCart { Id = id };
        snapshot.Fold(e);
        return snapshot;
    }
}

public partial class AsyncEvolvedCartProjection: SingleStreamProjection<AsyncEvolvedCart, string>
{
    public override ValueTask<AsyncEvolvedCart> EvolveAsync(AsyncEvolvedCart snapshot, string id, IQuerySession session,
        IEvent e, CancellationToken cancellation)
    {
        snapshot ??= new AsyncEvolvedCart { Id = id };
        snapshot.Fold(e);
        return new ValueTask<AsyncEvolvedCart>(snapshot);
    }
}

public partial class DecidedCartProjection: SingleStreamProjection<DecidedCart, string>
{
    public override ValueTask<(DecidedCart, ActionType)> DetermineActionAsync(IQuerySession session,
        DecidedCart snapshot, string identity, IIdentitySetter<DecidedCart, string> identitySetter,
        IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        snapshot ??= new DecidedCart { Id = identity };
        foreach (var e in events) snapshot.Fold(e);
        return new ValueTask<(DecidedCart, ActionType)>((snapshot, ActionType.Store));
    }
}
