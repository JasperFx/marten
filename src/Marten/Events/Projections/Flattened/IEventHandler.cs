#nullable enable
using System;
using System.Collections.Generic;
using Marten.Events.CodeGeneration;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal interface IEventHandler
{
    Type EventType { get; }
    IEventHandlingFrame BuildFrame(EventGraph events, Table table);

    bool AssertValid(EventGraph events, out string? message);

    IEnumerable<ISchemaObject> BuildObjects(EventGraph events, Table table);
}
