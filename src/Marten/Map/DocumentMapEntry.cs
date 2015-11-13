
namespace Marten.Map
{
    public class DocumentMapEntry<T> : IDocumentMapEntry
    {
        public DocumentIdentity Id { get; }

        public T Document { get; }
    
        object IDocumentMapEntry.Document => Document;

        public string OriginalJson { get; private set; }

        public DocumentMapEntry(DocumentIdentity id, T document, string originalJson)
        {
            Id = id;
            OriginalJson = originalJson;
            Document = document;
        }

        public void Updated(string json)
        {
            OriginalJson = json;
        }
    }
}