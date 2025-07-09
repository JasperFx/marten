using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Internal.Storage;
using Marten.Schema;

namespace Marten.Events.Fetching;

internal class AsyncFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IDocumentStorage<TDoc, TId> storage, IEventIdentityStrategy<TId> identity, StoreOptions options,
        [NotNullWhen(true)]out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        if (options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            if (projection is MultiStreamProjection<TDoc, TId>)
            {
                throw new InvalidOperationException(
                    $"The aggregate type {typeof(TDoc).FullNameInCode()} is the subject of a multi-stream projection and cannot be used with FetchForWriting");
            }



            if (projection.Scope == AggregationScope.MultiStream)
            {
                throw new InvalidOperationException(
                    $"The aggregate type {typeof(TDoc).FullNameInCode()} is the subject of a multi-stream projection and cannot be used with FetchForWriting");
            }

            if (projection.Lifecycle == ProjectionLifecycle.Async)
            {
                var mapping = options.Storage.FindMapping(typeof(TDoc)) as DocumentMapping;
                if (mapping != null && mapping.Metadata.Revision.Enabled)
                {
                    plan = new FetchAsyncPlan<TDoc, TId>(options.EventGraph, identity, storage);
                    return true;
                }
            }
        }

        plan = default;
        return false;
    }
}
