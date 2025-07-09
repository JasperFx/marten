using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    internal FetchInlinedPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy)
    {
        _events = events;
        _identityStrategy = identityStrategy;
    }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;

    private static IDocumentStorage<TDoc, TId> findDocumentStorage(QuerySession session)
    {
        IDocumentStorage<TDoc, TId>? storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.StorageFor<TDoc, TId>();
        }

        return storage;
    }

}
