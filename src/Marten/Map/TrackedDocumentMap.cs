using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Marten.Map
{
    public class TrackedDocumentMap : IDocumentMap
    {
        private readonly Dictionary<DocumentIdentity, IDocumentMapEntry> _map = new Dictionary<DocumentIdentity, IDocumentMapEntry>();
        private readonly ISerializer _serializer;
        private readonly IList<DocumentChange> _deletes = new List<DocumentChange>();

        public TrackedDocumentMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public void Store<T>(object id, T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var identity = new DocumentIdentity(typeof(T), id);
            IDocumentMapEntry existing;
            if (_map.TryGetValue(identity, out existing))
            {
                if (!ReferenceEquals(existing.Document, document))
                {
                    throw new InvalidOperationException($"Document '{typeof(T).FullName}' with same Id already added to the session.");
                }
                return;
            }

            _map[identity] = new DocumentMapEntry<T>(identity, document, null);
        }

        public T Loaded<T>(object id, T document, string originalJson)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var identity = new DocumentIdentity(typeof(T), id);
            IDocumentMapEntry existing;
            if (_map.TryGetValue(identity, out existing))
            {
                return (T)existing.Document;
            }

            _map[identity] = new DocumentMapEntry<T>(identity, document, originalJson);
            return document;
        }

        public DocumentMapEntry<T> Get<T>(object id)
        {
            var identity = new DocumentIdentity(typeof(T), id);

            IDocumentMapEntry value;
            return _map.TryGetValue(identity, out value) ? (DocumentMapEntry<T>) value : null;
        }

        public IEnumerable<DocumentChange> GetChanges()
        {
            return _map.Values
                .Select(CreateUpdate)
                .Where(update => update != null)
                .Union(_deletes)
                .ToArray();
        }

        private DocumentUpdate CreateUpdate(IDocumentMapEntry entry)
        {
            var json = _serializer.ToJson(entry.Document);

            if (ShouldUpdate(entry, json))
            {
                return new DocumentUpdate(entry.Document, json, entry.Id);
            }
            return null;
        }

        private static bool ShouldUpdate(IDocumentMapEntry entry, string json)
        {
            if (entry.OriginalJson == null) return true;

            // todo this can clearly be improved. I was thinking of generated code that extracts the state <string, string>[] from and object for more performant comparison
            var documentJObject = JObject.Parse(json);
            var originalJObject = JObject.Parse(entry.OriginalJson);

            return !JToken.DeepEquals(documentJObject, originalJObject);
        }

        public void ChangesApplied(IEnumerable<DocumentChange> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));

            foreach (var change in changes)
            {
                var documentUpdate = change as DocumentUpdate;
                if (documentUpdate != null)
                {
                    _map[documentUpdate.Id].Updated(documentUpdate.Json);
                }
                else
                {
                    _deletes.Remove(change);
                }
            }
        }

        public void DeleteDocument<T>(T document)
        {
            var change = new DocumentDelete(document);
            _deletes.Add(change);
        }

        public void DeleteById<T>(object id)
        {
            var change = new DocumentDeleteById(typeof(T), id);
            _deletes.Add(change);
        }
    }
}