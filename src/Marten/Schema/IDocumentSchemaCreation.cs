namespace Marten.Schema
{
    public interface IDocumentSchemaCreation
    {
        void CreateSchema(IDocumentSchema schema, DocumentMapping mapping);
        void RunScript(string script);
    }
}