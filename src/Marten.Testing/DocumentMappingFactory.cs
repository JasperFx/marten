using Marten.Schema;

namespace Marten.Testing
{
    public static class DocumentMappingFactory
    {
        public static DocumentMapping For<T>(string databaseSchemaName = StoreOptions.DefaultDatabaseSchemaName)
        {
            var storeOptions = new StoreOptions { DatabaseSchemaName = databaseSchemaName };

            return new DocumentMapping(typeof(T), storeOptions);
        }
    }
}