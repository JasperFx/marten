using System.Collections.Generic;
using System.Linq;

namespace Marten.Map
{
    public class NonTrackedDocumentMap : IDocumentMap
    {
        private readonly IList<IDocumentMapEntry> _updates = new List<IDocumentMapEntry>();
        private readonly IList<DocumentChange> _deletes = new List<DocumentChange>();
        private readonly ISerializer _serializer;

        public NonTrackedDocumentMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public DocumentMapEntry<T> Get<T>(object id)
        {
            return null; // documents are not tracked, we never return a document from the map
        }

        public IEnumerable<DocumentChange> GetChanges()
        {
            return _updates.Select(CreateUpdate)
                .Union(_deletes)
                .ToList();
        }

        private DocumentUpdate CreateUpdate(IDocumentMapEntry entry)
        {
            var json = _serializer.ToJson(entry.Document);
            return new DocumentUpdate(entry.Document, json, entry.Id);
        }

        public void ChangesApplied(IEnumerable<DocumentChange> changes)
        {
            foreach (var change in changes)
            {
                var documentUpdate = change as DocumentUpdate;
                if (documentUpdate != null)
                {
                    _updates.RemoveAll(entry => entry.Document == documentUpdate.Document);
                }
                else
                {
                    _deletes.Remove(change);
                }
            }
        }

        public void Store<T>(object id, T document)
        {
            var identity = new DocumentIdentity(typeof(T), id);

            var entry = new DocumentMapEntry<T>(identity, document, null);
            _updates.Add(entry);
        }

        public T Loaded<T>(object id, T document, string originalJson)
        {
            // loaded documents are not tracked nor returned from the map
            return document;
        }

        public void DeleteDocument<T>(T document)
        {
            _deletes.Add(new DocumentDelete(document));
        }

        public void DeleteById<T>(object id)
        {
            _deletes.Add(new DocumentDeleteById(typeof(T), id));
        }
    }
}