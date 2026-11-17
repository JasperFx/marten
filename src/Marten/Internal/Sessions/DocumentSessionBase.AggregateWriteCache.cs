#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events;
using JasperFx.Events.Fetching;

using Marten.Services;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    private AggregateWriteCacheSession? _aggregateWriteCache;
    private List<Action<IChangeSet>>? _aggregateWriteCacheAdvances;

    /// <summary>
    ///     jasperfx#1050: this unit of work's dealings with the aggregate write cache. A fetch takes its
    ///     baseline here and registers the aggregate it hands out; nothing reaches the cache until
    ///     <see cref="SaveChangesAsync" /> has committed a transaction this session owns.
    /// </summary>
    /// <remarks>
    ///     A <see cref="NestedTenantSession" /> shares its parent's, because it shares the parent's unit of
    ///     work and commit. The keys carry the tenant.
    /// </remarks>
    internal virtual AggregateWriteCacheSession AggregateWriteCache => _aggregateWriteCache ??= new();

    /// <summary>
    ///     For a fetch whose aggregate the commit itself moves on, which is the Inline lifecycle: run against
    ///     the committed change set before the write-back, to <see cref="AggregateWriteCacheSession.Advance" />
    ///     to the instance the inline projection produced, or to discard the entry.
    /// </summary>
    internal virtual void OnAggregateCommitted(Action<IChangeSet> advance)
    {
        (_aggregateWriteCacheAdvances ??= new()).Add(advance);
    }

    private void discardAggregateWrites()
    {
        _aggregateWriteCacheAdvances = null;
        _aggregateWriteCache?.Discard();
    }

    /// <summary>
    ///     After <see cref="ExecuteBatchAsync" /> succeeded. Only a commit this session owns publishes
    ///     anything: a session enlisted in a transaction somebody else commits (an external transaction,
    ///     an ambient scope) drops what it owed, because a flush the owner then rolls back would leave
    ///     entries holding events that never happened.
    /// </summary>
    private void publishAggregateWrites(IChangeSet commit)
    {
        var cache = _aggregateWriteCache;
        var advances = _aggregateWriteCacheAdvances;
        _aggregateWriteCacheAdvances = null;

        if (cache is not { HasPendingWrites: true }) return;

        if (HasSurvivingTransaction)
        {
            cache.Discard();
            return;
        }

        if (advances != null)
        {
            foreach (var advance in advances)
            {
                advance(commit);
            }
        }

        cache.Flush();
    }
}
