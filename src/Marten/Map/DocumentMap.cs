using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Marten.Map
{
    public class DocumentMap : IDocumentMap
    {
        private readonly Dictionary<DocumentIdentity, IDocumentMapEntry> _map = new Dictionary<DocumentIdentity, IDocumentMapEntry>();
        private readonly ISerializer _serializer;

        public DocumentMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public void Set<T>(object id, T document, string originalJson = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var identity = new DocumentIdentity(typeof(T), id);
            if (_map.ContainsKey(identity))
            {
                if (!ReferenceEquals(_map[identity].Document, document))
                {
                    throw new InvalidOperationException($"Document '{typeof(T).FullName}' with same Id already added to the session.");
                }
                return;
            }

            _map[identity] = new DocumentMapEntry<T>(identity, document, originalJson);
        }

        public DocumentMapEntry<T> Get<T>(object id)
        {
            IDocumentMapEntry value;
            var identity = new DocumentIdentity(typeof(T), id);

            return _map.TryGetValue(identity, out value) ? (DocumentMapEntry<T>) value : null;
        }

        public IEnumerable<DocumentUpdate> GetUpdates()
        {
            return _map.Values
                .Select(CreateUpdate)
                .Where(update => update != null)
                .ToArray();
        }

        private DocumentUpdate CreateUpdate(IDocumentMapEntry entry)
        {
            var json = _serializer.ToJson(entry.Document);

            if (ShouldUpdate(entry, json))
            {
                return new DocumentUpdate(entry.Id, entry.Document, json);
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

        public void Updated(IEnumerable<DocumentUpdate> updates)
        {
            if (updates == null) throw new ArgumentNullException(nameof(updates));

            foreach (var update in updates)
            {
                _map[update.Id].Updated(update.Json);
            }
        }
    }
}