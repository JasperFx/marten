#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface ISingleStreamSlicer{}

[Obsolete("Make this die")]
public interface ISingleStreamSlicer<TDoc, TId> : ISingleStreamSlicer
{
    // This is only done for running Inline. Fugly. Make this go away
    IReadOnlyList<EventSlice<TDoc, TId>> Transform(IQuerySession querySession,
        IEnumerable<StreamAction> streams);
}


