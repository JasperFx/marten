using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Storage;

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

    public async Task ApplyEvents(ProjectionUpdateBatch batch, IProjection projection, DocumentStore store,
        CancellationToken cancellationToken)
    {
        await using var operations = new ProjectionDocumentSession(store, batch,
            new SessionOptions { Tracking = DocumentTracking.None, Tenant = _tenant });
        await projection.ApplyAsync(operations, _actions, cancellationToken).ConfigureAwait(false);
    }
}
