using System;
using JasperFx.Events.Projections;
using Marten;

namespace ModularConfigTests.SatelliteA;

/// <summary>
/// Satellite-owned <see cref="IConfigureMarten"/> that registers this
/// assembly's projection with the host's <see cref="Marten.StoreOptions"/>.
/// Composes via DI: the main host wires this class as a singleton, then
/// Marten's <c>AsyncConfigureMartenApplication</c> calls
/// <see cref="Configure"/> after <c>AddMarten()</c> and before the store
/// is built.
/// </summary>
public class OrdersConfig : IConfigureMarten
{
    public void Configure(IServiceProvider services, StoreOptions options)
    {
        options.Projections.Add<OrderProjection>(ProjectionLifecycle.Inline);
    }
}
