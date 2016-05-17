using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;

namespace Marten.Testing
{
    public static class StorageTypesCache
    {
        private static readonly Task<List<Type>> _loader; 

        static StorageTypesCache()
        {
            _loader = Task.Factory.StartNew(() =>
            {
                using (var store = DocumentStore.For(_ =>
                {
                    _.Connection(ConnectionSource.ConnectionString);

                    _.RegisterDocumentType<Target>();

                    // TODO -- maybe add the other user types here later?
                    _.RegisterDocumentType<User>();
                    _.RegisterDocumentType<Issue>();
                    _.RegisterDocumentType<Company>();
                    _.RegisterDocumentType<IntDoc>();
                    _.RegisterDocumentType<LongDoc>();

                }))
                {
                    return store.Advanced.PrecompileAllStorage().Select(x => x.GetType()).ToList();
                }
            });
        }

        public static IList<Type> PrebuiltStorage()
        {
            _loader.Wait(10.Seconds());
            return _loader.Result;
        } 
    }

    public class TestingDocumentStore : DocumentStore
    {
        public static int SchemaCount = 0;
        private static readonly object _locker = new object();

        public new static IDocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Serializer<JilSerializer>();


            configure(options);

            

            var store = new TestingDocumentStore(options);
            store.Advanced.Clean.CompletelyRemoveAll();

            return store;
        }


        public static IDocumentStore Basic()
        {
            return For(_ =>
            {
            });
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