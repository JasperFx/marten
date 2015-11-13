namespace Marten.Map
{
    internal interface IDocumentMapEntry
    {
        DocumentIdentity Id { get; }
        string OriginalJson { get; }
        object Document { get; }
        void Updated(string json);
    }
}