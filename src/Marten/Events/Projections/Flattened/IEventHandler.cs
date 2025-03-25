using System;
using System.Collections.Generic;
using JasperFx.Events;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal interface IEventHandler
{
    Type EventType { get; }

    bool AssertValid(EventGraph events, out string? message);

    IEnumerable<ISchemaObject> BuildObjects(EventGraph events, Table table);

    void Handle(IDocumentOperations operations, IEvent e);

    void Compile(EventGraph events, Table table);
}
