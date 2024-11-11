using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;

namespace Marten.Events.Aggregation.Rebuilds;

public class StringIdentifiedSingleStreamRebuilder<TDoc, TId> : IReplayExecutor
{
    public StringIdentifiedSingleStreamRebuilder(DocumentStore store, IMartenDatabase database)
    {
        throw new NotImplementedException();
    }

    public async Task StartAsync(SubscriptionExecutionRequest request, ISubscriptionController controller,
        CancellationToken cancellation)
    {
        throw new NotImplementedException();
    }
}