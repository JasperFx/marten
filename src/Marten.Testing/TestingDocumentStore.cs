using System;
using System.Threading;

namespace Marten.Testing
{
    public class TestingDocumentStore : DocumentStore
    {
        public static int SchemaCount = 0;
        private static object _locker = new object();

        public new static IDocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);

            lock (_locker)
            {
                options.DatabaseSchemaName = "Test_" + SchemaCount++;
            }
            

            configure(options);

            

            var store = new TestingDocumentStore(options);
            store.Advanced.Clean.CompletelyRemoveAll();

            return store;
        }


        public static IDocumentStore Basic()
        {
            return For(_ => { });
        }

        public static IDocumentStore DefaultSchema()
        {
            var store = For(_ =>
            {
                _.DatabaseSchemaName = StoreOptions.DefaultDatabaseSchemaName;
            });
            return store;
        }

        private TestingDocumentStore(StoreOptions options) : base(options)
        {
        }

        public override void Dispose()
        {
            var schemaName = Advanced.Options.DatabaseSchemaName;

            if (schemaName != StoreOptions.DefaultDatabaseSchemaName)
            {
                var sql = $"DROP SCHEMA {schemaName} CASCADE;";
                using (var conn = Advanced.OpenConnection())
                {
                    conn.Execute(cmd => cmd.CommandText = sql);
                }
            }

            
            base.Dispose();


        }
    }
}