namespace Marten.Schema
{
    public interface IDocumentSchemaCreation
    {
        void CreateSchema(IDocumentSchema schema, IDocumentMapping mapping);
        void RunScript(string script);
    }
}