using Marten.Generation;

namespace Marten.Schema
{
    public interface IDocumentSchemaObjects : ISchemaObjects
    {

        TableDefinition StorageTable();
        
    }
}