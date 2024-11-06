using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Storage;

#nullable enable

namespace Marten.Events.Daemon;

internal class TenantActionGroup
{
    private readonly List<StreamAction> _actions;
    private readonly Tenant _tenant;

    public TenantActionGroup(Tenant tenant, IEnumerable<StreamAction> actions)
    {
        _tenant = tenant;
        _actions = new List<StreamAction>(actions);
        foreach (var action in _actions) action.TenantId = _tenant.TenantId;
    }

    public async Task ApplyEvents(
        ProjectionUpdateBatch batch,
        IProjection projection,
        AsyncOptions asyncOptions,
        DocumentStore store,
        CancellationToken cancellationToken)
    {
        var tracking = asyncOptions.EnableDocumentTrackingByIdentity
            ? DocumentTracking.IdentityOnly
            : DocumentTracking.None;

        await using var operations = new ProjectionDocumentSession(store, batch,
            new SessionOptions { Tracking = tracking, Tenant = _tenant }, batch.Mode);
        await projection.ApplyAsync(operations, _actions, cancellationToken).ConfigureAwait(false);
    }
}
