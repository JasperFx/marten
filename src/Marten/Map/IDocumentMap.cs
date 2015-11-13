using System.Collections.Generic;

namespace Marten.Map
{
    public interface IDocumentMap
    {
        void Set<T>(object id, T document, string originalJson = null);

        DocumentMapEntry<T> Get<T>(object id);

        IEnumerable<DocumentUpdate> GetUpdates();

        void Updated(IEnumerable<DocumentUpdate> updates);
    }
}