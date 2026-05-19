using System;
using Marten.Events.Aggregation;

namespace ModularConfigTests.SatelliteA;

// `partial` is required so the JasperFx.Events.SourceGenerator can emit
// the dispatcher's `Evolve` override into this class declaration. Post-#276
// the SG-emitted [GeneratedEvolver] is the only apply-dispatch path; the
// SG silently skips emission for non-partial classes, after which the
// runtime fail-fast at AssembleAndAssertValidity throws.
public partial class OrderProjection : SingleStreamProjection<Order, Guid>
{
    public void Apply(OrderPlaced @event, Order snapshot)
    {
        snapshot.Amount = @event.Amount;
    }

    public void Apply(OrderShipped @event, Order snapshot)
    {
        snapshot.IsShipped = true;
    }
}
