using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Patching;

namespace Marten.Services
{
    public class ChangeSet : IChangeSet
    {
        public readonly IList<object> Updated = new List<object>();
        public readonly IList<object> Inserted = new List<object>();
        public readonly IList<EventStream> Streams = new List<EventStream>();
        public readonly IList<IStorageOperation> Operations = new List<IStorageOperation>();

        IEnumerable<object> IChangeSet.Updated => Updated;

        IEnumerable<object> IChangeSet.Inserted => Inserted;

        IEnumerable<IDeletion> IChangeSet.Deleted => Operations.OfType<IDeletion>();
        

        public DocumentChange[] Changes;

        public ChangeSet(DocumentChange[] documentChanges)
        {
            Changes = documentChanges;
            Updated.AddRange(documentChanges.Select(x => x.Document));
        }

        public IEnumerable<IEvent> GetEvents()
        {
            return Streams.SelectMany(s => s.Events);
        }

        IEnumerable<PatchOperation> IChangeSet.Patches => Operations.OfType<PatchOperation>();
        public IEnumerable<EventStream> GetStreams()
        {
            return Streams;
        }
    }
}