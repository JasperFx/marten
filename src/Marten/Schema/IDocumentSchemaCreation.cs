namespace Marten.Schema
{
    public interface IDocumentSchemaCreation
    {
        void CreateSchema(IDocumentStorage storage);
    }
}