using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Internal.Storage;
using Marten.Schema;

namespace Marten.Events.Fetching;

internal class AsyncFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IEventIdentityStrategy<TId> identity,
        StoreOptions options,
        [NotNullWhen(true)] out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        // #5144: a null identity strategy means planning came down the natural-key branch. These
        // lifecycle planners match on the projection alone, so without this they would happily hand
        // back a plan holding that null and fail later with a bare NullReferenceException.
        if (identity is null)
        {
            plan = null;
            return false;
        }

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
                if (options.Storage.FindMapping(typeof(TDoc)) is DocumentMapping { Metadata.Revision.Enabled: true })
                {
                    plan = new FetchAsyncPlan<TDoc, TId>(options.EventGraph, identity, options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly));
                    return true;
                }
            }
        }

        plan = default;
        return false;
    }
}
