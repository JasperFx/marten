using Marten.Events;
using System.Collections.Generic;
using Marten.Patching;

namespace Marten.Services
{
    public interface IChangeSet
    {
        IEnumerable<object> Updated { get; } 
        IEnumerable<object> Inserted { get; }
        IEnumerable<Delete> Deleted { get; }
        IEnumerable<IEvent> GetEvents();

        IEnumerable<PatchOperation> Patches { get; }
        IEnumerable<EventStream> GetStreams();
    }
}