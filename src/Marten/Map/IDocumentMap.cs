using System.Collections.Generic;

namespace Marten.Map
{
    public interface IDocumentMap
    {
        void Store<T>(object id, T document);

        T Loaded<T>(object id, T document, string originalJson);

        void DeleteDocument<T>(T document);
        void DeleteById<T>(object id);

        DocumentMapEntry<T> Get<T>(object id);

        IEnumerable<DocumentChange> GetChanges();

        void ChangesApplied(IEnumerable<DocumentChange> changes);
    }
}