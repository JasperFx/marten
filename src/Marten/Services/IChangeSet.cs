#nullable enable
using System.Collections.Generic;
using Marten.Events;

namespace Marten.Services;

public interface IChangeSet
{
    IEnumerable<object> Updated { get; }
    IEnumerable<object> Inserted { get; }
    IEnumerable<IDeletion> Deleted { get; }

    IEnumerable<IEvent> GetEvents();

    IEnumerable<StreamAction> GetStreams();

    IChangeSet Clone();
}
