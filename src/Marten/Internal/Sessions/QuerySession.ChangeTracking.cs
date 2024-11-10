#nullable enable
using System;
using System.Collections.Generic;
using Weasel.Core.Operations.DirtyTracking;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    // TODO -- this should be on DocumentSessionBase somehow
    public Dictionary<Type, object> ItemMap { get; internal set; } = new();


    public void MarkAsAddedForStorage(object id, object document)
    {
        foreach (var listener in Listeners) listener.DocumentAddedForStorage(id, document);
    }

    public void MarkAsDocumentLoaded(object id, object? document)
    {
        if (document == null)
        {
            return;
        }

        foreach (var listener in Listeners) listener.DocumentLoaded(id, document);
    }

    // TODO -- try to make this only built out for dirty checking sessions
    public IList<IChangeTracker> ChangeTrackers { get; } = new List<IChangeTracker>();
}
