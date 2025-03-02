using System.Collections.Generic;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
///     Optional interface to expose additional schema objects to be
///     built as part of the event store
/// </summary>
public interface IProjectionSchemaSource
{
    IEnumerable<ISchemaObject> CreateSchemaObjects(EventGraph events);
}
