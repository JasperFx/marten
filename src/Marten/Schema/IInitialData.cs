namespace Marten.Schema
{
    public interface IInitialData
    {
        void Populate(IDocumentStore store);
    }
}