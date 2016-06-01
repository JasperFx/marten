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
        public readonly IList<Delete> Deleted = new List<Delete>();
        public readonly IList<EventStream> Streams = new List<EventStream>();
        public readonly IList<PatchOperation> Patched = new List<PatchOperation>();

        IEnumerable<object> IChangeSet.Updated => Updated;

        IEnumerable<object> IChangeSet.Inserted => Inserted;

        IEnumerable<Delete> IChangeSet.Deleted => Deleted;
        

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

        IEnumerable<PatchOperation> IChangeSet.Patches => Patched;
    }
}