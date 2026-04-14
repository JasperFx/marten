using System;
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Sessions;

namespace Marten.Events.Fetching;

internal static class FetchPlanHelper
{
    /// <summary>
    ///     Find any events that have been appended to the given stream in the current
    ///     session but not yet committed via SaveChangesAsync().
    /// </summary>
    public static IReadOnlyList<IEvent>? FindPendingEvents<TId>(DocumentSessionBase session, TId id)
    {
        if (id is Guid guidId && session.WorkTracker.TryFindStream(guidId, out var guidStream))
        {
            return guidStream.Events;
        }

        if (id is string stringId && session.WorkTracker.TryFindStream(stringId, out var stringStream))
        {
            return stringStream.Events;
        }

        return null;
    }
}
