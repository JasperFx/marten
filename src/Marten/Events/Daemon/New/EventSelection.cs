using System;
using System.Collections.Generic;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon.New;

public class EventSelection
{
    public List<Type> EventTypes { get; } = new();
    public Type AggregateType { get; set; }
    public bool UseArchived { get; set; } = false;

    // TODO -- might be an opportunity for the new strategy for event naming
    public IEnumerable<ISqlFragment> CreateFragments()
    {
        throw new NotImplementedException();
    }
}
