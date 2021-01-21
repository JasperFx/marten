using System.Collections.Generic;
using Marten.Events;
using Marten.Patching;

namespace Marten.Services
{
    public interface IChangeSet
    {
        IEnumerable<object> Updated { get; }
        IEnumerable<object> Inserted { get; }
        IEnumerable<IDeletion> Deleted { get; }

        IEnumerable<IEvent> GetEvents();

        IEnumerable<PatchOperation> Patches { get; }

        IEnumerable<StreamAction> GetStreams();

        IChangeSet Clone();
    }
}
