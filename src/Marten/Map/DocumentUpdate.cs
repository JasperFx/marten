namespace Marten.Map
{
    public class DocumentUpdate
    {
        public DocumentIdentity Id { get; }
        public object Document { get; }
        public string Json { get; }

        public DocumentUpdate(DocumentIdentity id, object document, string json)
        {
            Id = id;
            Document = document;
            Json = json;
        }
    }
}